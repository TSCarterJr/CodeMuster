namespace CodeMuster.Domain;

/// <summary>One defect the model reported against a path inside the unit it analyzed (D11).</summary>
public sealed record Finding(
    string Path,
    int LineStart,
    int LineEnd,
    Severity Severity,
    string Category,
    string Claim,
    string Evidence,
    double Confidence,
    string LensId);
