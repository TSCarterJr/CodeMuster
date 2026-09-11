using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>What <see cref="CompositeMapper"/> produced: one map merged from every mapper that returned, and which languages were mapped and which failed.</summary>
/// <param name="Map">Symbols, edges, entry points, and diagnostics of every mapper in run order, with resolution counts summed.</param>
/// <param name="FailedLanguages">Languages whose mapper threw; their files stay whole-file units at low fidelity (D25).</param>
/// <param name="MappedLanguages">Languages whose mapper returned a map.</param>
public sealed record CompositeMap(CodeMap Map, IReadOnlyList<string> FailedLanguages, IReadOnlyList<string> MappedLanguages)
{
    /// <summary>Resolved share of every call site in the returned maps (D09): 1.0 when they found no call sites, null when no mapper returned a map, so a scan whose mappers all threw never reports full resolution.</summary>
    public double? ResolutionRate =>
        MappedLanguages.Count == 0 ? null
        : Map.Resolution.Resolved + Map.Resolution.Unresolved == 0 ? 1.0
        : (double)Map.Resolution.Resolved / (Map.Resolution.Resolved + Map.Resolution.Unresolved);

    /// <summary>The merged most frequent unresolved names, or null when no mapper returned a map.</summary>
    public IReadOnlyList<string>? TopUnresolvedNames => MappedLanguages.Count == 0 ? null : Map.Resolution.TopUnresolvedNames;
}
