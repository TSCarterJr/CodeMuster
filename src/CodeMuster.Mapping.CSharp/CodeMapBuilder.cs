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

    public async Task AddAsync(Solution solution, CancellationToken cancellationToken)
    {
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            var path = document.FilePath is null ? null : RepoPath.Normalize(Path.GetRelativePath(repoRoot, document.FilePath));
            if (path is null || !included.Contains(path) || !seen.Add(path) || await document.GetSemanticModelAsync(cancellationToken) is not { } model)
            {
                continue;
            }

            var root = await model.SyntaxTree.GetRootAsync(cancellationToken);
            foreach (var node in root.DescendantNodes())
            {
                if (DeclaredMethod(node, model, cancellationToken) is not { } method || Id(method) is not { } id)
                {
                    continue;
                }

                symbols.TryAdd(id, new Symbol(id, path, Lines(node), KindOf(node), Signature(node), BodyHash(node)));
                AddCalls(id, node, model, cancellationToken);
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
            [],
            new ResolutionStats(0, 0, []),
            []);
    }

    private void AddCalls(string from, SyntaxNode declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var node in declaration.DescendantNodes(child => !IsNameOf(child)))
        {
            switch (node)
            {
                case InvocationExpressionSyntax when !IsNameOf(node):
                case BaseObjectCreationExpressionSyntax:
                case ConstructorInitializerSyntax:
                    AddTargets(from, model.GetSymbolInfo(node, cancellationToken).Symbol);
                    break;
                case SimpleNameSyntax name:
                    AddNameTargets(from, name, model.GetSymbolInfo(name, cancellationToken).Symbol);
                    break;
                case ElementAccessExpressionSyntax element when model.GetSymbolInfo(element, cancellationToken).Symbol is IPropertySymbol indexer:
                    AddAccessorTargets(from, element, indexer);
                    break;
            }
        }
    }

    private void AddNameTargets(string from, SimpleNameSyntax name, ISymbol? symbol)
    {
        var outer = name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => (ExpressionSyntax)name,
        };
        switch (symbol)
        {
            case IPropertySymbol property:
                AddAccessorTargets(from, outer, property);
                break;
            case IMethodSymbol method when outer.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != outer:
                AddTargets(from, method);
                break;
        }
    }

    private void AddAccessorTargets(string from, ExpressionSyntax expression, IPropertySymbol property)
    {
        var (read, write) = expression.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left == expression => (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression), true),
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax when expression.Parent.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression => (true, true),
            _ => (true, false),
        };
        if (read)
        {
            AddTargets(from, property.GetMethod);
        }

        if (write)
        {
            AddTargets(from, property.SetMethod);
        }
    }

    private void AddTargets(string from, ISymbol? symbol)
    {
        if (symbol is IMethodSymbol method && Id(method) is { } id)
        {
            edges.Add(new Edge(from, id, EdgeKind.Call));
        }
    }

    private static string? Id(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition.GetDocumentationCommentId();

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
