using System.Collections.Concurrent;
using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CodeMuster.Mapping.CSharp;

internal sealed class Dispatcher(Solution solution, IReadOnlyDictionary<string, List<INamedTypeSymbol>> bindings)
{
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<(string To, EdgeKind Kind)>>> cache = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<(string To, EdgeKind Kind)>> TargetsAsync(IMethodSymbol callee, string calleeId, CancellationToken cancellationToken) =>
        cache.GetOrAdd(calleeId, _ => FindAsync(callee.OriginalDefinition, calleeId, cancellationToken));

    private async Task<IReadOnlyList<(string To, EdgeKind Kind)>> FindAsync(IMethodSymbol method, string methodId, CancellationToken cancellationToken)
    {
        if (method.IsStatic || method.ContainingType is not { } type || !method.Locations.Any(location => location.IsInSource))
        {
            return [];
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            if (type.GetDocumentationCommentId() is { } interfaceId && bindings.TryGetValue(interfaceId, out var bound))
            {
                return Targets(bound.SelectMany(implementation => Implementations(implementation, interfaceId, methodId)), EdgeKind.Bound);
            }

            return Targets(await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: cancellationToken), EdgeKind.Implements);
        }

        if ((method.IsVirtual || method.IsAbstract || method.IsOverride) && !method.IsSealed && !type.IsSealed)
        {
            return Targets(await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: cancellationToken), EdgeKind.Overrides);
        }

        return [];
    }

    private static IEnumerable<ISymbol> Implementations(INamedTypeSymbol implementation, string interfaceId, string methodId) =>
        implementation.AllInterfaces
            .Where(face => face.OriginalDefinition.GetDocumentationCommentId() == interfaceId)
            .SelectMany(face => face.GetMembers())
            .Where(member => member.OriginalDefinition.GetDocumentationCommentId() == methodId)
            .Select(implementation.FindImplementationForInterfaceMember)
            .OfType<ISymbol>();

    private static List<(string To, EdgeKind Kind)> Targets(IEnumerable<ISymbol> symbols, EdgeKind kind) =>
        symbols.Select(CodeMapBuilder.Id).OfType<string>().Distinct(StringComparer.Ordinal).Select(id => (id, kind)).ToList();
}
