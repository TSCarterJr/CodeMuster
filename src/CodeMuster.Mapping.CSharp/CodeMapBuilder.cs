using System.Text;
using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMuster.Mapping.CSharp;

internal sealed class CodeMapBuilder(string repoRoot, IReadOnlyList<string> paths)
{
    private readonly HashSet<string> included = paths.Select(RepoPath.Normalize).ToHashSet(StringComparer.Ordinal);
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Symbol> symbols = new(StringComparer.Ordinal);
    private readonly HashSet<Edge> edges = [];
    private readonly HashSet<EntryPoint> entryPoints = [];
    private readonly Dictionary<string, int> unresolved = new(StringComparer.Ordinal);
    private int resolved;

    public async Task AddAsync(Solution solution, CancellationToken cancellationToken)
    {
        var dispatcher = new Dispatcher(solution, await Bindings.FindAsync(solution, cancellationToken));
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            var path = document.FilePath is null ? null : RepoPath.Normalize(Path.GetRelativePath(repoRoot, document.FilePath));
            if (path is null || !included.Contains(path) || !seen.Add(path) || await document.GetSemanticModelAsync(cancellationToken) is not { } model)
            {
                continue;
            }

            var root = await model.SyntaxTree.GetRootAsync(cancellationToken);
            entryPoints.UnionWith(EntryPoints.Find(root, model, cancellationToken));
            foreach (var node in root.DescendantNodes())
            {
                if (DeclaredMethod(node, model, cancellationToken) is not { } method || Id(method) is not { } from)
                {
                    continue;
                }

                symbols.TryAdd(from, new Symbol(from, path, Lines(node), KindOf(node), Signature(node), BodyHash(node)));
                CountCallSites(node, model, cancellationToken);
                foreach (var callee in Callees(node, model, cancellationToken))
                {
                    if (Id(callee) is not { } to)
                    {
                        continue;
                    }

                    edges.Add(new Edge(from, to, EdgeKind.Call));
                    foreach (var (target, kind) in await dispatcher.TargetsAsync(callee, to, cancellationToken))
                    {
                        edges.Add(new Edge(from, target, kind));
                    }
                }
            }
        }
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
            []);
    }

    private void CountCallSites(SyntaxNode declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var node in declaration.DescendantNodes(child => !IsNameOf(child)))
        {
            var name = node switch
            {
                InvocationExpressionSyntax invocation when !IsNameOf(node) => InvokedName(invocation.Expression),
                ObjectCreationExpressionSyntax creation => TypeName(creation.Type),
                ImplicitObjectCreationExpressionSyntax => "new",
                _ => null,
            };
            if (name is null)
            {
                continue;
            }

            if (model.GetSymbolInfo(node, cancellationToken).Symbol is null)
            {
                unresolved[name] = unresolved.GetValueOrDefault(name) + 1;
            }
            else
            {
                resolved++;
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
                _ => [],
            };
            foreach (var callee in callees.OfType<IMethodSymbol>())
            {
                yield return callee;
            }
        }
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

    private static string BodyHash(SyntaxNode node)
    {
        var tokens = node is AccessorDeclarationSyntax accessor
            ? Header(accessor.Parent!.Parent!).Concat(accessor.DescendantTokens())
            : node.DescendantTokens();
        return Hashing.Sha256Hex(string.Join(" ", tokens.Select(token => token.Text)));
    }

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
}
