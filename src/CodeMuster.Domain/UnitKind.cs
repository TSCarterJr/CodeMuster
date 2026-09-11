namespace CodeMuster.Domain;

/// <summary>The closed set of unit kinds; the ledger and the loop never branch on it (D06).</summary>
public enum UnitKind
{
    /// <summary>One whole file: every file in file mode, and in slice mode a file no mapper covers or with no symbols (D25).</summary>
    File,
    /// <summary>One entry point plus its call path (D07).</summary>
    Slice,
    /// <summary>The symbols of one mapped file that no slice reaches, or the whole file when none is reached (D25).</summary>
    Orphan,
    /// <summary>One finding to confirm or refute.</summary>
    Verify,
}
