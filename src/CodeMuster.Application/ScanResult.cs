namespace CodeMuster.Application;

/// <summary>What one <see cref="Scan"/> found and changed.</summary>
/// <param name="HeadCommit">Commit the tree was at.</param>
/// <param name="FilesIncluded">Files that have a unit.</param>
/// <param name="FilesExcluded">Files recorded with an exclusion reason.</param>
/// <param name="UnitsCreated">Units that entered the work pool for the first time.</param>
/// <param name="UnitsStale">Units left stale after the scan.</param>
/// <param name="UnitsTotal">Units present after the scan, retired ones excluded.</param>
public sealed record ScanResult(string HeadCommit, int FilesIncluded, int FilesExcluded, int UnitsCreated, int UnitsStale, int UnitsTotal);
