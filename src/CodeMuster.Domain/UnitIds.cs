using System.Globalization;

namespace CodeMuster.Domain;

/// <summary>Naming rules for unit ids.</summary>
public static class UnitIds
{
    /// <summary>Id of the file unit for a repo-relative path.</summary>
    public static string File(string path) => "file:" + path;

    /// <summary>Id of the orphan unit for a repo-relative path (D25).</summary>
    public static string Orphan(string path) => "orphan:" + path;

    /// <summary>Id of the verify unit that tests one finding.</summary>
    public static string Verify(long findingId) => "verify:" + findingId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Id of the fix unit for a repo-relative path (D37).</summary>
    public static string Fix(string path) => "fix:" + path;

    /// <summary>Id of the dependency unit for a manifest (D38).</summary>
    public static string Dependency(string manifest) => "dependency:" + manifest;

    /// <summary>Id of the browser review for a UI source path.</summary>
    public static string Ux(string path) => "ux:" + path;

    /// <summary>Id of static reachability analysis for a source path.</summary>
    public static string DeadCode(string path) => "dead_code:" + path;

    /// <summary>Id of the impact unit for a changed symbol (D67).</summary>
    public static string Impact(string symbolId) => "impact:" + symbolId;

    /// <summary>Id of the duplicate unit for a normalized body hash (D68); the language is added only when the hash has copies in more than one language.</summary>
    public static string Duplicate(string normalizedHash, string? language = null) => "duplicate:" + normalizedHash + (language is null ? "" : ":" + language);

    /// <summary>Id of the slice unit that starts at an entry point's symbol.</summary>
    public static string Slice(string entrySymbolId) => "slice:" + entrySymbolId;
}
