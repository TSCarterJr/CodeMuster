namespace CodeMuster.Domain;

/// <summary>The whole JSON document the model returns for a call that reviewed several small units together: one analysis per unit (D80).</summary>
public sealed record BatchAnalysisResponse(IReadOnlyList<UnitAnalysis> Units);

/// <summary>One unit's analysis inside a <see cref="BatchAnalysisResponse"/>, in the shape of an <see cref="AnalysisResponse"/>.</summary>
/// <param name="Unit">The unit id the pack listed.</param>
/// <param name="Summary">One or two sentences on what the unit does.</param>
/// <param name="Findings">Its findings (D11).</param>
public sealed record UnitAnalysis(string Unit, string Summary, IReadOnlyList<Finding> Findings);
