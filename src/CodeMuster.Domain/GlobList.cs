using System.Runtime.CompilerServices;

namespace CodeMuster.Domain;

/// <summary>An ordered list of globs, each compiled once on first use, for matching many paths.</summary>
public sealed class GlobList
{
    private readonly Lazy<Glob[]> globs;

    /// <summary>Holds the patterns and compiles them on the first match, so a list that is never matched costs nothing.</summary>
    public GlobList(IReadOnlyList<string> patterns)
    {
        Patterns = patterns;
        globs = new(() => patterns.Select(pattern => new Glob(pattern)).ToArray());
    }

    /// <summary>The patterns as written.</summary>
    public IReadOnlyList<string> Patterns { get; }

    /// <summary>The first pattern, in list order, that matches the path; null when none does.</summary>
    public string? FirstMatch(string path) => globs.Value.FirstOrDefault(glob => glob.IsMatch(path))?.Pattern;

    /// <summary>True when any pattern matches the path.</summary>
    public bool AnyMatch(string path) => globs.Value.Any(glob => glob.IsMatch(path));

    /// <summary>Equal when both hold the same pattern list instance, so a record holding a <see cref="GlobList"/> keeps the equality its plain pattern list gave it.</summary>
    public override bool Equals(object? obj) => obj is GlobList other && ReferenceEquals(other.Patterns, Patterns);

    /// <summary>Hashes the pattern list instance, matching <see cref="Equals(object?)"/>.</summary>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(Patterns);
}
