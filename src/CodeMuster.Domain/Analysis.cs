namespace CodeMuster.Domain;

/// <summary>One attempt to analyze a unit; findings are stored alongside it.</summary>
/// <param name="UnitId">The unit analyzed.</param>
/// <param name="Fingerprint">Unit fingerprint the response was produced against.</param>
/// <param name="LensHash">Hash of the lenses in effect.</param>
/// <param name="CreatedAt">UTC ISO 8601.</param>
/// <param name="Succeeded">False when the response was unusable.</param>
/// <param name="Summary">The one-line unit summary from the response, when it succeeded.</param>
/// <param name="Error">Why the response was unusable, when it failed.</param>
/// <param name="By">What produced it (D35), or null when nothing recorded it, as when an agent drives the loop by hand.</param>
public sealed record Analysis(string UnitId, string Fingerprint, string LensHash, string CreatedAt, bool Succeeded, string? Summary, string? Error, AgentIdentity? By = null);
