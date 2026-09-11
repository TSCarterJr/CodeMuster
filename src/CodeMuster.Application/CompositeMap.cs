using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>What <see cref="CompositeMapper"/> produced: one map merged from every mapper that returned, and the languages whose mapper threw.</summary>
/// <param name="Map">Symbols, edges, entry points, and diagnostics of every mapper in run order, with resolution counts summed.</param>
/// <param name="FailedLanguages">Languages whose mapper threw; their files stay whole-file units at low fidelity (D25).</param>
public sealed record CompositeMap(CodeMap Map, IReadOnlyList<string> FailedLanguages);
