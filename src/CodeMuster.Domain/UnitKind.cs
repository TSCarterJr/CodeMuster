namespace CodeMuster.Domain;

/// <summary>The closed set of unit kinds; the ledger and the loop never branch on it (D06).</summary>
public enum UnitKind
{
    /// <summary>One included source file.</summary>
    File,
    /// <summary>One entry point plus its call path (D07).</summary>
    Slice,
    /// <summary>An included file reachable from no slice.</summary>
    Orphan,
    /// <summary>One finding to confirm or refute.</summary>
    Verify,
}
