namespace CodeMuster.Domain;

/// <summary>One scan of the tree.</summary>
/// <param name="StartedAt">UTC ISO 8601.</param>
/// <param name="HeadCommit">Commit the tree was at.</param>
/// <param name="FilesIncluded">Files that got a unit.</param>
/// <param name="FilesExcluded">Files recorded with an exclusion reason.</param>
/// <param name="UnitsTotal">Units present after the scan, retired ones excluded.</param>
/// <param name="ResolutionRate">Mapper call-site resolution rate (D09), or null when no mapper ran.</param>
/// <param name="TopUnresolvedNames">Most frequent call names the mappers could not resolve, or null when no mapper ran.</param>
public sealed record ScanRun(string StartedAt, string HeadCommit, int FilesIncluded, int FilesExcluded, int UnitsTotal, double? ResolutionRate, IReadOnlyList<string>? TopUnresolvedNames = null);
