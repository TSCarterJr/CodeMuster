namespace CodeMuster.Domain;

/// <summary>The whole JSON document the model returns for a verify unit: a verdict on the finding and the reason for it.</summary>
public sealed record VerifyResponse(Verdict Verdict, string Reason);
