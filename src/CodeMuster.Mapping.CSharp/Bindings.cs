using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMuster.Mapping.CSharp;

internal static class Bindings
{
    private static readonly FrozenSet<string> Registrations = new[]
    {
        "AddScoped", "AddTransient", "AddSingleton", "TryAddScoped", "TryAddTransient", "TryAddSingleton",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Namespaces = new[]
    {
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Extensions",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static async Task<IReadOnlyDictionary<string, List<INamedTypeSymbol>>> FindAsync(Solution solution, CancellationToken cancellationToken)
    {
        var bindings = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var invocations = root?.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsRegistrationName).ToList() ?? [];
            if (invocations.Count == 0 || await document.GetSemanticModelAsync(cancellationToken) is not { } model)
            {
                continue;
            }

            foreach (var invocation in invocations)
            {
                if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method || !Namespaces.Contains(method.ContainingNamespace.ToDisplayString()))
                {
                    continue;
                }

                ITypeSymbol?[] types = method.TypeArguments.Length == 2
                    ? [.. method.TypeArguments]
                    : [.. invocation.ArgumentList.Arguments.OrderBy(argument => (model.GetOperation(argument, cancellationToken) as IArgumentOperation)?.Parameter?.Ordinal).Select(argument => argument.Expression).OfType<TypeOfExpressionSyntax>().Select(typeOf => model.GetTypeInfo(typeOf.Type, cancellationToken).Type)];
                if (types is [INamedTypeSymbol { TypeKind: TypeKind.Interface } service, INamedTypeSymbol implementation]
                    && service.OriginalDefinition.GetDocumentationCommentId() is { } serviceId)
                {
                    if (!bindings.TryGetValue(serviceId, out var implementations))
                    {
                        bindings[serviceId] = implementations = [];
                    }

                    implementations.Add(implementation.OriginalDefinition);
                }
            }
        }

        return bindings;
    }

    private static bool IsRegistrationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax access => Registrations.Contains(access.Name.Identifier.Text),
        SimpleNameSyntax name => Registrations.Contains(name.Identifier.Text),
        _ => false,
    };
}
