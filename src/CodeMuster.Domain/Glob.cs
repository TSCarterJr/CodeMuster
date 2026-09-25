using System.Text;
using System.Text.RegularExpressions;

namespace CodeMuster.Domain;

/// <summary>A gitignore-style glob compiled once for matching many normalized repo-relative paths.</summary>
public sealed class Glob
{
    private readonly Regex regex;
    private readonly bool fileNameOnly;

    /// <summary>Compiles the pattern: "**" spans whole segments, "*" and "?" stay within one segment, and a pattern without a slash is matched against the file name only.</summary>
    public Glob(string pattern)
    {
        var normalizedPattern = RepoPath.Normalize(pattern);
        Pattern = pattern;
        fileNameOnly = !normalizedPattern.Contains('/');
        regex = new Regex(ToRegex(normalizedPattern), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    /// <summary>The pattern as written.</summary>
    public string Pattern { get; }

    /// <summary>Returns true when the path matches this glob.</summary>
    public bool IsMatch(string path)
    {
        var normalizedPath = RepoPath.Normalize(path);
        var subject = fileNameOnly
            ? normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..]
            : normalizedPath;

        return regex.IsMatch(subject);
    }

    /// <summary>Compiles the pattern and matches one path; hold a <see cref="Glob"/> or <see cref="GlobList"/> to match many.</summary>
    public static bool IsMatch(string pattern, string path) => new Glob(pattern).IsMatch(path);

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
