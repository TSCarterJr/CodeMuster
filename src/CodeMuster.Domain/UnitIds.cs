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

    /// <summary>Id of the slice unit that starts at an entry point's symbol.</summary>
    public static string Slice(string entrySymbolId) => "slice:" + entrySymbolId;
}
