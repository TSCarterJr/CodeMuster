namespace CodeMuster.Domain;

/// <summary>The whole JSON document the model returns for one unit: a one-line summary plus its findings.</summary>
public sealed record AnalysisResponse(string Summary, IReadOnlyList<Finding> Findings);
