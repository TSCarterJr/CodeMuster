namespace CodeMuster.Domain;

/// <summary>One finding the fixer chose not to change.</summary>
/// <param name="Finding">The finding's ledger id, as the pack gave it.</param>
/// <param name="Reason">Why it was left alone.</param>
public sealed record DeclinedFix(long Finding, string Reason);

/// <summary>What a fix unit's agent reports back (D37).</summary>
/// <param name="Summary">One line on what changed in the file.</param>
/// <param name="Addressed">Ids of the findings the change addresses.</param>
/// <param name="Declined">Findings left alone, each with a reason.</param>
public sealed record FixResponse(string Summary, IReadOnlyList<long> Addressed, IReadOnlyList<DeclinedFix> Declined);
