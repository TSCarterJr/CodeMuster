using System.Text;
using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMuster.Mapping.CSharp;

internal sealed class CodeMapBuilder(string repoRoot, IReadOnlyList<string> paths)
{
    private readonly HashSet<string> included = paths.Select(RepoPath.Normalize).ToHashSet(StringComparer.Ordinal);
    private readonly HashSet<string> mappedProjects = new(RoslynMapper.ProjectPathComparer);
    private readonly Dictionary<string, Symbol> symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Symbol> declarations = new(StringComparer.Ordinal);
    private readonly HashSet<Edge> edges = [];
    private readonly HashSet<EntryPoint> entryPoints = [];
    private readonly Dictionary<string, int> unresolved = new(StringComparer.Ordinal);
    private int resolved;

    // A project loaded again by a later workspace (another solution, or a project outside it that references it) keeps its
    // file path and its name, which carries the target framework, so each target framework is still mapped once.
    public async Task AddAsync(Solution solution, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("finding dependency injection bindings");
        var dispatcher = new Dispatcher(solution, await Bindings.FindAsync(solution, cancellationToken));
        var documents = solution.Projects
            .Where(project => mappedProjects.Add((project.FilePath ?? project.Id.Id.ToString()) + "|" + project.Name))
            .ToList()
            .SelectMany(project => project.Documents)
            .Where(document => document.FilePath is not null)
            .Select(document => (Document: document, Path: RepoPath.Normalize(Path.GetRelativePath(repoRoot, document.FilePath!))))
            .Where(item => included.Contains(item.Path))
            .ToList();
        var perProject = documents.GroupBy(item => item.Document.Project.Id).ToDictionary(group => group.Key, group => group.Count());
        var results = new DocumentMap?[documents.Count];
        var merged = 0;
        ProjectId? project = null;
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };

        // Documents are bound in parallel but merged strictly in list order, so the first definition of a shared id and the
        // progress lines are the same as a sequential walk.
        await Parallel.ForEachAsync(Enumerable.Range(0, documents.Count), options, async (index, token) =>
        {
            var result = await MapDocumentAsync(documents[index].Document, documents[index].Path, dispatcher, token);
            lock (results)
            {
                results[index] = result;
                while (merged < documents.Count && results[merged] is { } ready)
                {
                    results[merged] = null;
                    var document = documents[merged].Document;
                    if (document.Project.Id != project)
                    {
                        project = document.Project.Id;
                        progress?.Report($"reading {document.Project.Name}, {perProject[project]} files");
                    }

                    Merge(ready);
                    merged++;
                    if (merged * 10 / documents.Count > (merged - 1) * 10 / documents.Count)
                    {
                        progress?.Report($"mapped {merged}/{documents.Count} files");
                    }
                }
            }
        });
    }

    private void Merge(DocumentMap document)
    {
        foreach (var symbol in document.Symbols)
        {
            symbols.TryAdd(symbol.Id, symbol);
        }

        foreach (var declaration in document.Declarations)
        {
            declarations.TryAdd(declaration.Id, declaration);
        }

        edges.UnionWith(document.Edges);
        entryPoints.UnionWith(document.EntryPoints);
        resolved += document.Resolved;
        foreach (var (name, count) in document.Unresolved)
        {
            unresolved[name] = unresolved.GetValueOrDefault(name) + count;
        }
    }

    private static async Task<DocumentMap> MapDocumentAsync(Document document, string path, Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        var map = new DocumentMap();
        if (await document.GetSemanticModelAsync(cancellationToken) is not { } model)
        {
            return map;
        }

        var root = await model.SyntaxTree.GetRootAsync(cancellationToken);
        map.EntryPoints.AddRange(EntryPoints.Find(root, model, cancellationToken));
        foreach (var node in root.DescendantNodes())
        {
            foreach (var (_, declaration) in Declarations(node, model, path, cancellationToken))
            {
                map.Declarations.Add(declaration);
            }

            if (DeclaredMethod(node, model, cancellationToken) is not { } method || Id(method) is not { } from)
            {
                continue;
            }

            map.Symbols.Add(new Symbol(from, path, Lines(node), KindOf(node), Signature(node), BodyHash(node), NormalizedHash(node)));
            CountCallSites(node, model, map, cancellationToken);
            foreach (var callee in Callees(node, model, cancellationToken))
            {
                if (Id(callee) is not { } to)
                {
                    continue;
                }

                map.Edges.Add(new Edge(from, to, EdgeKind.Call));
                foreach (var (target, kind) in await dispatcher.TargetsAsync(callee, to, cancellationToken))
                {
                    map.Edges.Add(new Edge(from, target, kind));
                }
            }
        }

        return map;
    }

    public CodeMap Build()
    {
        return new CodeMap(
            symbols.Values.OrderBy(symbol => symbol.Path, StringComparer.Ordinal).ThenBy(symbol => symbol.Range.StartLine).ToList(),
            edges
                .Where(edge => symbols.ContainsKey(edge.From) && symbols.ContainsKey(edge.To))
                .OrderBy(edge => edge.From, StringComparer.Ordinal)
                .ThenBy(edge => edge.To, StringComparer.Ordinal)
                .ThenBy(edge => edge.Kind)
                .ToList(),
            entryPoints
                .Where(entry => symbols.ContainsKey(entry.SymbolId))
                .OrderBy(entry => entry.Display, StringComparer.Ordinal)
                .ThenBy(entry => entry.SymbolId, StringComparer.Ordinal)
                .ToList(),
            new ResolutionStats(
                resolved,
                unresolved.Values.Sum(),
                unresolved.OrderByDescending(name => name.Value).ThenBy(name => name.Key, StringComparer.Ordinal).Take(20).Select(name => name.Key).ToList()),
            [])
        {
            Declarations = declarations.Values
                .OrderBy(declaration => declaration.Path, StringComparer.Ordinal)
                .ThenBy(declaration => declaration.Range.StartLine)
                .ThenBy(declaration => declaration.Id, StringComparer.Ordinal)
                .ToList(),
        };
    }

    // A partial type is declared once, at its first definition in document order, as a symbol shared by two documents is.
    private static IEnumerable<(SyntaxNode Node, Symbol Declaration)> Declarations(SyntaxNode node, SemanticModel model, string path, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax type when TypeKind(type) is { } kind:
                var header = type.DescendantTokens().TakeWhile(token => token != type.OpenBraceToken && token != type.SemicolonToken).ToList();
                return Declared(node, model.GetDeclaredSymbol(node, cancellationToken), path, kind, Collapse(header), header);
            case DelegateDeclarationSyntax @delegate:
                return Declared(node, model.GetDeclaredSymbol(node, cancellationToken), path, "delegate",
                    Collapse(@delegate.DescendantTokens().Where(token => token != @delegate.SemicolonToken)), @delegate.DescendantTokens());
            case EnumMemberDeclarationSyntax member:
                return Declared(node, model.GetDeclaredSymbol(node, cancellationToken), path, "enum_member",
                    Collapse(member.DescendantTokens().TakeWhile(token => member.EqualsValue is null || token.SpanStart < member.EqualsValue.SpanStart)), member.DescendantTokens());
            case BaseFieldDeclarationSyntax field:
                var prefix = field.DescendantTokens().TakeWhile(token => token.SpanStart < field.Declaration.Variables[0].SpanStart).ToList();
                return field.Declaration.Variables.SelectMany(variable => Declared(
                    variable,
                    model.GetDeclaredSymbol(variable, cancellationToken),
                    path,
                    field is EventFieldDeclarationSyntax ? "event" : model.GetDeclaredSymbol(variable, cancellationToken) is IFieldSymbol { IsConst: true } ? "constant" : "field",
                    Collapse(prefix) + " " + variable.Identifier.Text,
                    [.. prefix, .. variable.DescendantTokens(), field.SemicolonToken],
                    Lines(field)));
            case PropertyDeclarationSyntax property:
                var accessors = property.AccessorList?.Accessors.Select(accessor => Collapse(Header(accessor).Where(token => token != accessor.SemicolonToken)) + ";") ?? ["get;"];
                return Declared(node, model.GetDeclaredSymbol(node, cancellationToken), path, "property",
                    $"{Collapse(Header(property))} {{ {string.Join(" ", accessors)} }}", WithoutBodies(property));
            case EventDeclarationSyntax @event:
                return Declared(node, model.GetDeclaredSymbol(node, cancellationToken), path, "event", Collapse(Header(@event)), WithoutBodies(@event));
            case ParameterSyntax { Parent.Parent: RecordDeclarationSyntax record } parameter:
                var positional = (model.GetDeclaredSymbol(record, cancellationToken) as INamedTypeSymbol)?.GetMembers(parameter.Identifier.Text).OfType<IPropertySymbol>()
                    .FirstOrDefault(member => member.DeclaringSyntaxReferences.Any(reference => reference.Span == parameter.Span && reference.SyntaxTree == parameter.SyntaxTree));
                return Declared(node, positional, path, "property", Collapse(parameter.DescendantTokens().TakeWhile(token => parameter.Default is null || token.SpanStart < parameter.Default.SpanStart)), parameter.DescendantTokens());
            default:
                return [];
        }
    }

    private static IEnumerable<(SyntaxNode Node, Symbol Declaration)> Declared(
        SyntaxNode node, ISymbol? symbol, string path, string kind, string signature, IEnumerable<SyntaxToken> hashed, LineRange? lines = null)
    {
        if (symbol is not null && Id(symbol) is { } id)
        {
            yield return (node, new Symbol(id, path, lines ?? Lines(node), kind, signature, Hashing.Sha256Hex(string.Join(" ", hashed.Select(token => token.Text))), null));
        }
    }

    private static string? TypeKind(BaseTypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => "record",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        _ => null,
    };

    // Accessor bodies are symbols of their own, so a declaration's hash changes with its shape and initializer, not with the code its accessors run.
    private static IEnumerable<SyntaxToken> WithoutBodies(SyntaxNode node) =>
        node.DescendantTokens(child => !(child is BlockSyntax or ArrowExpressionClauseSyntax && child.Parent is AccessorDeclarationSyntax or BasePropertyDeclarationSyntax));

    private static void CountCallSites(SyntaxNode declaration, SemanticModel model, DocumentMap map, CancellationToken cancellationToken)
    {
        foreach (var node in declaration.DescendantNodes(child => !IsNameOf(child)))
        {
            var name = node switch
            {
                InvocationExpressionSyntax invocation when !IsNameOf(node) => InvokedName(invocation.Expression),
                ObjectCreationExpressionSyntax creation => TypeName(creation.Type),
                ImplicitObjectCreationExpressionSyntax => "new",
                _ when OperatorInfo(node, model, cancellationToken).Symbol is IMethodSymbol method => method.Name,
                _ when OperatorInfo(node, model, cancellationToken).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault() is { } candidate => candidate.Name,
                _ => null,
            };
            if (name is null)
            {
                continue;
            }

            if (model.GetSymbolInfo(node, cancellationToken).Symbol is null)
            {
                map.Unresolved[name] = map.Unresolved.GetValueOrDefault(name) + 1;
            }
            else
            {
                map.Resolved++;
            }
        }
    }

    private static string InvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => expression.ToString(),
    };

    private static string TypeName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => type.ToString(),
    };

    internal static string? Id(ISymbol symbol) =>
        (symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol).OriginalDefinition.GetDocumentationCommentId();

    private static IEnumerable<IMethodSymbol> Callees(SyntaxNode declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var node in declaration.DescendantNodes(child => !IsNameOf(child)))
        {
            IEnumerable<ISymbol?> callees = node switch
            {
                InvocationExpressionSyntax when !IsNameOf(node) => [model.GetSymbolInfo(node, cancellationToken).Symbol],
                BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax => [model.GetSymbolInfo(node, cancellationToken).Symbol],
                SimpleNameSyntax name => NameCallees(name, model.GetSymbolInfo(name, cancellationToken).Symbol),
                ElementAccessExpressionSyntax element when model.GetSymbolInfo(element, cancellationToken).Symbol is IPropertySymbol indexer => Accessors(element, indexer),
                _ => [OperatorInfo(node, model, cancellationToken).Symbol],
            };
            foreach (var callee in callees.OfType<IMethodSymbol>())
            {
                yield return callee;
            }
        }
    }

    private static SymbolInfo OperatorInfo(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AssignmentExpressionSyntax or CastExpressionSyntax))
        {
            return default;
        }

        var info = model.GetSymbolInfo(node, cancellationToken);
        return info.Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion }
            || info.CandidateSymbols.OfType<IMethodSymbol>().Any(method => method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
            ? info
            : default;
    }

    private static IEnumerable<ISymbol?> NameCallees(SimpleNameSyntax name, ISymbol? symbol)
    {
        var outer = name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => (ExpressionSyntax)name,
        };
        return symbol switch
        {
            IPropertySymbol property => Accessors(outer, property),
            IMethodSymbol method when outer.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != outer => [method],
            _ => [],
        };
    }

    private static IEnumerable<ISymbol?> Accessors(ExpressionSyntax expression, IPropertySymbol property)
    {
        var assignment = expression.Parent as AssignmentExpressionSyntax;
        var assigned = assignment is not null && assignment.Left == expression;
        if (!assigned || !assignment!.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            yield return property.GetMethod;
        }

        if (assigned || expression.Parent?.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
        {
            yield return property.SetMethod;
        }
    }

    private static bool IsNameOf(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } };

    private static IMethodSymbol? DeclaredMethod(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken) => node switch
    {
        BaseMethodDeclarationSyntax { Body: not null } or BaseMethodDeclarationSyntax { ExpressionBody: not null } => model.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol,
        AccessorDeclarationSyntax { Body: not null } or AccessorDeclarationSyntax { ExpressionBody: not null } => model.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol,
        PropertyDeclarationSyntax { ExpressionBody: not null } or IndexerDeclarationSyntax { ExpressionBody: not null } => (model.GetDeclaredSymbol(node, cancellationToken) as IPropertySymbol)?.GetMethod,
        _ => null,
    };

    private static string KindOf(SyntaxNode node) => node switch
    {
        ConstructorDeclarationSyntax => "constructor",
        OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => "operator",
        AccessorDeclarationSyntax or BasePropertyDeclarationSyntax => "accessor",
        _ => "method",
    };

    private static LineRange Lines(SyntaxNode node)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        return new LineRange(span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    private static string Signature(SyntaxNode node)
    {
        var type = node.Ancestors().OfType<TypeDeclarationSyntax>().First();
        var header = Collapse(type.DescendantTokens().TakeWhile(token => token != type.OpenBraceToken));
        var member = node switch
        {
            AccessorDeclarationSyntax accessor => $"{Collapse(Header(accessor.Parent!.Parent!))} {{ {Collapse(Header(accessor))}; }}",
            BasePropertyDeclarationSyntax property => $"{Collapse(Header(property))} {{ get; }}",
            _ => Collapse(Header(node)),
        };
        return header + "\n" + member;
    }

    private static string BodyHash(SyntaxNode node) =>
        Hashing.Sha256Hex(string.Join(" ", DeclarationTokens(node).Select(token => token.Text)));

    private static string NormalizedHash(SyntaxNode node) =>
        Hashing.Sha256Hex(string.Join(" ", DeclarationTokens(node).Select(Normalized)));

    private static string Normalized(SyntaxToken token) =>
        token.IsKind(SyntaxKind.IdentifierToken) ? "$id"
        : token.IsKind(SyntaxKind.InterpolatedStringTextToken) || (token.Parent is LiteralExpressionSyntax && !SyntaxFacts.IsKeywordKind(token.Kind())) ? "$literal"
        : token.Text;

    private static IEnumerable<SyntaxToken> DeclarationTokens(SyntaxNode node) =>
        node is AccessorDeclarationSyntax accessor
            ? Header(accessor.Parent!.Parent!).Concat(accessor.DescendantTokens())
            : node.DescendantTokens();

    private static IEnumerable<SyntaxToken> Header(SyntaxNode node)
    {
        SyntaxNode? body = node switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
            PropertyDeclarationSyntax property => (SyntaxNode?)property.AccessorList ?? property.ExpressionBody,
            IndexerDeclarationSyntax indexer => (SyntaxNode?)indexer.AccessorList ?? indexer.ExpressionBody,
            EventDeclarationSyntax @event => @event.AccessorList,
            _ => null,
        };
        return node.DescendantTokens().TakeWhile(token => body is null || token.SpanStart < body.SpanStart);
    }

    private static string Collapse(IEnumerable<SyntaxToken> tokens)
    {
        var text = new StringBuilder();
        var previous = default(SyntaxToken);
        foreach (var token in tokens)
        {
            if (text.Length > 0 && (previous.HasTrailingTrivia || token.HasLeadingTrivia))
            {
                text.Append(' ');
            }

            text.Append(token.Text);
            previous = token;
        }

        return text.ToString();
    }

    private sealed class DocumentMap
    {
        public List<Symbol> Symbols { get; } = [];

        public List<Symbol> Declarations { get; } = [];

        public List<Edge> Edges { get; } = [];

        public List<EntryPoint> EntryPoints { get; } = [];

        public Dictionary<string, int> Unresolved { get; } = new(StringComparer.Ordinal);

        public int Resolved { get; set; }
    }
}
