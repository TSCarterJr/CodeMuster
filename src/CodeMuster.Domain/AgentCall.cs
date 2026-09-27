namespace CodeMuster.Domain;

/// <summary>One paid agent call, recorded whether or not its response was usable (D63). The cost is fixed when the call is recorded, so a later price change never rewrites past spend.</summary>
/// <param name="CreatedAt">UTC ISO 8601.</param>
/// <param name="Run">The invocation the call belonged to, such as <c>run 2026-09-27T10:00:00.0000000Z</c>.</param>
/// <param name="UnitId">The unit the call worked on, or null for a call outside the unit loop.</param>
/// <param name="Kind">The kind of that unit, or null.</param>
/// <param name="By">The harness, requested model and effort (D35).</param>
/// <param name="Usage">What the harness reported the call used.</param>
/// <param name="Succeeded">True when the response was recorded; false when the call failed or its response was rejected.</param>
/// <param name="CostUsd">API-equivalent cost in US dollars, or null when the call is unpriced.</param>
/// <param name="CostSource">Where the cost came from: <c>harness</c>, <c>config</c>, or <c>table &lt;date checked&gt;</c>; null when unpriced.</param>
public sealed record AgentCall(string CreatedAt, string Run, string? UnitId, UnitKind? Kind, AgentIdentity By, AgentUsage Usage, bool Succeeded, decimal? CostUsd, string? CostSource);
