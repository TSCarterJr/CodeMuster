namespace CodeMuster.Application;

/// <summary>What a fix run did (D37).</summary>
/// <param name="Units">Files it recorded a result for.</param>
/// <param name="Fixed">Findings the agent changed code for.</param>
/// <param name="Declined">Findings it left alone, with reasons in the ledger.</param>
/// <param name="GaveUp">Units that used all their attempts.</param>
/// <param name="TestsRun">True when a test command gated every recorded fix; false when none was configured.</param>
public sealed record FixResult(int Units, int Fixed, int Declined, IReadOnlyList<string> GaveUp, bool TestsRun = false);
