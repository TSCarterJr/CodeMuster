namespace CodeMuster.Domain;

/// <summary>The code map the latest mapping scan stored in the ledger (D60). It holds no source text; readers take lines from the file or from git at <see cref="HeadCommit"/>.</summary>
/// <param name="HeadCommit">Commit the scan that took the map was at.</param>
/// <param name="ScannedAt">When that scan started, UTC ISO 8601.</param>
/// <param name="Map">Every symbol, edge and entry point the mappers returned, in their order, with resolution counts and diagnostics.</param>
/// <param name="MappedLanguages">Languages whose mapper returned a map.</param>
/// <param name="FailedLanguages">Languages whose mapper threw; their symbols are missing from the map and <see cref="CodeMap.Diagnostics"/> says why.</param>
public sealed record StoredCodeMap(string HeadCommit, string ScannedAt, CodeMap Map, IReadOnlyList<string> MappedLanguages, IReadOnlyList<string> FailedLanguages)
{
    /// <summary>True when a mapper failed or reported a diagnostic, so the map may lack symbols or edges. The UI-to-API join's diagnostics (<see cref="HttpCall.DiagnosticPrefix"/>) describe unmatched calls and endpoints, not missing code, so they do not count.</summary>
    public bool IsPartial => FailedLanguages.Count > 0 || Map.Diagnostics.Any(diagnostic => !diagnostic.StartsWith(HttpCall.DiagnosticPrefix, StringComparison.Ordinal));
}
