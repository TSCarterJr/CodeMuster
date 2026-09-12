namespace CodeMuster.Application;

/// <summary>What planning fix units found (D37).</summary>
/// <param name="Files">Files with at least one confirmed finding, one unit each.</param>
/// <param name="Findings">Confirmed findings those units cover.</param>
/// <param name="NeedingWork">Units not already fixed against the file's current content.</param>
public sealed record FixPlan(int Files, int Findings, int NeedingWork);
