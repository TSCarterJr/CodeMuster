namespace CodeMuster.Domain;

/// <summary>The whole JSON document the model returns for a verify unit holding several findings: one verdict per finding (D78).</summary>
public sealed record VerifyBatchResponse(IReadOnlyList<FindingVerdict> Verdicts);

/// <summary>The verdict on one finding of a batched verify unit and the reason for it.</summary>
/// <param name="Finding">The finding's ledger id, as the pack listed it.</param>
/// <param name="Verdict">Confirmed, refuted, unsure or resolved.</param>
/// <param name="Reason">The lines that settle it.</param>
public sealed record FindingVerdict(long Finding, Verdict Verdict, string Reason);
