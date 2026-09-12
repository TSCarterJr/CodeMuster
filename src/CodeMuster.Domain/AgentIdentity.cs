namespace CodeMuster.Domain;

/// <summary>What produced an analysis (D35): the harness, and the model and effort level it was asked for. A null model or effort means the harness's own default.</summary>
/// <param name="Agent">Harness name, such as <c>claude</c> or <c>codex</c>.</param>
/// <param name="Model">Model passed to the harness, verbatim.</param>
/// <param name="Effort">Effort or reasoning level passed to the harness, verbatim.</param>
public sealed record AgentIdentity(string Agent, string? Model = null, string? Effort = null);
