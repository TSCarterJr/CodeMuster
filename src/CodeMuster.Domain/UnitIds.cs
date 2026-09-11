namespace CodeMuster.Domain;

/// <summary>Naming rules for unit ids.</summary>
public static class UnitIds
{
    /// <summary>Id of the file unit for a repo-relative path.</summary>
    public static string File(string path) => "file:" + path;
}
