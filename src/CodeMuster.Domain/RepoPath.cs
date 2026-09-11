namespace CodeMuster.Domain;

/// <summary>Normalizes repo-relative paths to the one shape stored and compared everywhere: forward slashes, no leading "./", no trailing slash.</summary>
public static class RepoPath
{
    /// <summary>Returns the path with backslashes replaced, repeated slashes collapsed, a leading "./" stripped, and a trailing slash stripped.</summary>
    public static string Normalize(string path)
    {
        var result = path.Replace('\\', '/');
        while (result.Contains("//", StringComparison.Ordinal))
        {
            result = result.Replace("//", "/", StringComparison.Ordinal);
        }

        while (result.StartsWith("./", StringComparison.Ordinal))
        {
            result = result[2..];
        }

        return result.TrimEnd('/');
    }
}
