using System.Text;
using System.Text.RegularExpressions;

namespace CodeMuster.Domain;

/// <summary>Minimal gitignore-style glob matching over normalized repo-relative paths.</summary>
public static class Glob
{
    /// <summary>Returns true when the path matches the pattern: "**" spans whole segments, "*" and "?" stay within one segment, and a pattern without a slash is matched against the file name only.</summary>
    public static bool IsMatch(string pattern, string path)
    {
        var normalizedPattern = RepoPath.Normalize(pattern);
        var normalizedPath = RepoPath.Normalize(path);
        var subject = normalizedPattern.Contains('/')
            ? normalizedPath
            : normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];

        return new Regex(ToRegex(normalizedPattern), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking).IsMatch(subject);
    }

    private static string ToRegex(string pattern)
    {
        var segments = pattern.Split('/');
        var regex = new StringBuilder("^");
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                regex.Append(last ? ".*" : "(?:[^/]+/)*");
                continue;
            }

            foreach (var c in segments[i])
            {
                regex.Append(c switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(c.ToString()),
                });
            }

            if (!last)
            {
                regex.Append('/');
            }
        }

        return regex.Append('$').ToString();
    }
}
