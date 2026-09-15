using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Identifies supported UI source conventions without treating backend folders named UI as browser code.</summary>
public static class UiScope
{
    /// <summary>True for a supported UI path or an explicit include; explicit excludes take precedence.</summary>
    public static bool Applies(string path, IReadOnlyList<string>? include = null, IReadOnlyList<string>? exclude = null)
    {
        var normalized = RepoPath.Normalize(path);
        if (exclude?.Any(glob => Glob.IsMatch(glob, normalized)) == true) return false;
        if (include?.Any(glob => Glob.IsMatch(glob, normalized)) == true) return true;

        var lower = normalized.ToLowerInvariant();
        if (lower.Split('/').Any(segment => segment is "test" or "tests" or "__tests__")
            || lower.Contains(".test.", StringComparison.Ordinal)
            || lower.Contains(".spec.", StringComparison.Ordinal)) return false;

        return Path.GetExtension(lower) is ".tsx" or ".jsx" or ".vue" or ".svelte" or ".astro"
            or ".html" or ".htm" or ".razor" or ".cshtml" or ".css" or ".scss" or ".sass" or ".less"
            || lower.EndsWith(".component.ts", StringComparison.Ordinal);
    }

    /// <summary>Returns unique normalized UI paths in ordinal order.</summary>
    public static IReadOnlyList<string> Paths(IEnumerable<string> paths, IReadOnlyList<string>? include = null, IReadOnlyList<string>? exclude = null) =>
        paths.Select(RepoPath.Normalize).Where(path => Applies(path, include, exclude)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
