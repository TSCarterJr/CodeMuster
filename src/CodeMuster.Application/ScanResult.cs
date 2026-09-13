namespace CodeMuster.Application;

/// <summary>What one <see cref="Scan"/> found and changed.</summary>
/// <param name="HeadCommit">Commit the tree was at.</param>
/// <param name="FilesIncluded">Files that have a unit.</param>
/// <param name="FilesExcluded">Files recorded with an exclusion reason.</param>
/// <param name="UnitsCreated">Units that entered the work pool for the first time.</param>
/// <param name="UnitsStale">Units left stale after the scan.</param>
/// <param name="UnitsTotal">Units present after the scan, retired ones excluded.</param>
/// <param name="SliceMode">What slice mode built and how well the mappers resolved, or null when the scan ran in file mode.</param>
/// <param name="Vulnerabilities">What the dependency audit found, or null when it did not run (D38).</param>
public sealed record ScanResult(
    string HeadCommit,
    int FilesIncluded,
    int FilesExcluded,
    int UnitsCreated,
    int UnitsStale,
    int UnitsTotal,
    SliceModeResult? SliceMode = null,
    DependencyResult? Vulnerabilities = null);

/// <summary>The units a slice-mode <see cref="Scan"/> planned and what its mappers reported.</summary>
/// <param name="Slices">Slice units present after the scan.</param>
/// <param name="Orphans">Orphan units present after the scan.</param>
/// <param name="Files">Whole-file units present after the scan.</param>
/// <param name="ResolutionRate">Share of call sites the mappers resolved, 0 to 1, or null when no mapper returned a map (D09).</param>
/// <param name="Diagnostics">Why the map may be incomplete, including one line per mapper that failed; empty when nothing went wrong.</param>
public sealed record SliceModeResult(int Slices, int Orphans, int Files, double? ResolutionRate, IReadOnlyList<string> Diagnostics);
