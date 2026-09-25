using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>What the dependency audit found during a scan (D38).</summary>
/// <param name="Manifests">Manifests whose audit ran.</param>
/// <param name="Packages">Advisories recorded across them.</param>
/// <param name="BySeverity">How many of each severity, most serious first.</param>
/// <param name="Diagnostics">Manifests whose audit could not run, each naming the fix, and folders with lockfiles for more than one tool, naming the tool used.</param>
public sealed record DependencyResult(
    int Manifests,
    int Packages,
    IReadOnlyDictionary<Severity, int> BySeverity,
    IReadOnlyList<string> Diagnostics);
