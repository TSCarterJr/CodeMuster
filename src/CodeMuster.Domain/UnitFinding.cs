namespace CodeMuster.Domain;

/// <summary>A finding as the ledger currently holds it: from the most recent successful analysis of its unit, with the fingerprint that analysis was made against.</summary>
/// <param name="UnitId">The unit whose analysis reported the finding.</param>
/// <param name="Fingerprint">Unit fingerprint the analysis was produced against; differs from the unit's current fingerprint once the unit is stale.</param>
/// <param name="Finding">The finding itself (D11).</param>
public sealed record UnitFinding(string UnitId, string Fingerprint, Finding Finding);
