using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMuster.Mapping.CSharp;

// Collects the references in one document. Nodes are added in pre-order, so every container of a node is known before the node is.
internal sealed class ReferenceCollector(SemanticModel model, string path, List<Reference> references, CancellationToken cancellationToken)
{
    private readonly Dictionary<SyntaxNode, string> containers = [];
    private readonly Dictionary<ISymbol, string?> ids = new(SymbolEqualityComparer.Default);

    public void Contain(SyntaxNode node, string id) => containers.TryAdd(node, id);

    public void Add(SyntaxNode node)
    {
        switch (node)
        {
            case SimpleNameSyntax name when !name.IsVar && !InNamespaceOrUsing(name):
                AddName(name);
                break;
            case BaseObjectCreationExpressionSyntax creation when model.GetSymbolInfo(creation, cancellationToken).Symbol is IMethodSymbol constructor:
                if (creation is ObjectCreationExpressionSyntax { Type: var type })
                {
                    Record(constructor, ReferenceKind.Call, Identifier(type));
                }
                else
                {
                    Record(constructor, ReferenceKind.Call, creation.NewKeyword);
                    Record(constructor.ContainingType, ReferenceKind.Type, creation.NewKeyword);
                }

                break;
            case ConstructorInitializerSyntax initializer when model.GetSymbolInfo(initializer, cancellationToken).Symbol is IMethodSymbol constructor:
                Record(constructor, ReferenceKind.Call, initializer.ThisOrBaseKeyword);
                break;
            case PrimaryConstructorBaseTypeSyntax primary when model.GetSymbolInfo(primary, cancellationToken).Symbol is IMethodSymbol constructor:
                Record(constructor, ReferenceKind.Call, Identifier(primary.Type));
                break;
        }
    }

    private void AddName(SimpleNameSyntax name)
    {
        var info = model.GetSymbolInfo(name, cancellationToken);
        // A lone candidate is still the one symbol the name means; a member group (nameof, or an ambiguous overload) is not.
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 && info.CandidateReason != CandidateReason.MemberGroup ? info.CandidateSymbols[0] : null);
        if (symbol is null)
        {
            return;
        }

        var outer = Outer(name);
        if (outer.Parent is AttributeSyntax attribute && attribute.Name == outer)
        {
            Record(symbol is IMethodSymbol constructor ? constructor.ContainingType : symbol, ReferenceKind.Attribute, name.Identifier);
            return;
        }

        switch (symbol)
        {
            case IMethodSymbol:
                Record(symbol, ReferenceKind.Call, name.Identifier);
                break;
            case IFieldSymbol or IPropertySymbol or IEventSymbol:
                Record(symbol, IsWritten(outer) ? ReferenceKind.Write : ReferenceKind.Read, name.Identifier);
                break;
            case INamedTypeSymbol type:
                var kind = outer.Parent is BaseTypeSyntax { Parent: BaseListSyntax } baseType && baseType.Type == outer
                    ? type.TypeKind == TypeKind.Interface ? ReferenceKind.Implement : ReferenceKind.Inherit
                    : ReferenceKind.Type;
                Record(type, kind, name.Identifier);
                break;
        }
    }

    private void Record(ISymbol symbol, ReferenceKind kind, SyntaxToken at)
    {
        var target = (symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol).OriginalDefinition;
        if (target.DeclaringSyntaxReferences.IsEmpty)
        {
            return;
        }

        if (!ids.TryGetValue(target, out var to))
        {
            to = target.GetDocumentationCommentId();
            ids[target] = to;
        }

        var from = Container(at.Parent);
        if (to is null || to == from)
        {
            return;
        }

        var position = model.SyntaxTree.GetLineSpan(at.Span, cancellationToken).StartLinePosition;
        references.Add(new Reference(from, to, kind, path, position.Line + 1, position.Character + 1));
    }

    private string Container(SyntaxNode? node)
    {
        for (; node is not null; node = node.Parent)
        {
            if (containers.TryGetValue(node, out var id))
            {
                return id;
            }
        }

        return path;
    }

    private static SyntaxNode Outer(SimpleNameSyntax name)
    {
        SyntaxNode outer = name;
        while (outer.Parent is MemberAccessExpressionSyntax access && access.Name == outer
            || outer.Parent is QualifiedNameSyntax qualified && qualified.Right == outer
            || outer.Parent is AliasQualifiedNameSyntax alias && alias.Name == outer
            || outer.Parent is MemberBindingExpressionSyntax)
        {
            outer = outer.Parent;
        }

        return outer;
    }

    private static bool IsWritten(SyntaxNode outer) => outer.Parent switch
    {
        AssignmentExpressionSyntax assignment => assignment.Left == outer,
        PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => outer.Parent.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
            or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
        ArgumentSyntax argument => argument.RefKindKeyword.Kind() is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword,
        NameEqualsSyntax { Parent: AttributeArgumentSyntax } => true,
        _ => false,
    };

    // Names in namespace declarations and plain using directives only ever bind to namespaces, so they are not worth binding.
    private static bool InNamespaceOrUsing(SimpleNameSyntax name)
    {
        SyntaxNode top = name;
        while (top.Parent is NameSyntax parent)
        {
            top = parent;
        }

        return top.Parent is BaseNamespaceDeclarationSyntax or UsingDirectiveSyntax { StaticKeyword.RawKind: 0, Alias: null };
    }

    private static SyntaxToken Identifier(TypeSyntax type) => type switch
    {
        SimpleNameSyntax name => name.Identifier,
        QualifiedNameSyntax qualified => qualified.Right.Identifier,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier,
        _ => type.GetFirstToken(),
    };
}
