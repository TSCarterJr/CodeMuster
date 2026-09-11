namespace CodeMuster.Domain;

/// <summary>Naming rules for unit ids.</summary>
public static class UnitIds
{
    /// <summary>Id of the file unit for a repo-relative path.</summary>
    public static string File(string path) => "file:" + path;

    /// <summary>Id of the orphan unit for a repo-relative path (D25).</summary>
    public static string Orphan(string path) => "orphan:" + path;

    /// <summary>Id of the slice unit that starts at an entry point's symbol.</summary>
    public static string Slice(string entrySymbolId) => "slice:" + entrySymbolId;
}
