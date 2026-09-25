using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>One named set of audit instructions from <c>.codemuster/config.json</c>, scoped by globs and languages.</summary>
/// <param name="Id">Short name the model echoes back as <c>lens_id</c> on each finding.</param>
/// <param name="Instructions">What to look for; goes into the pack verbatim.</param>
/// <param name="Globs">Repo-relative globs the lens applies to; empty means every path.</param>
/// <param name="Languages">Language codes the lens applies to; empty means every language.</param>
public sealed record Lens(string Id, string Instructions, IReadOnlyList<string> Globs, IReadOnlyList<string> Languages)
{
    private readonly GlobList globs = new(Globs);

    /// <summary>Repo-relative globs the lens applies to; empty means every path.</summary>
    public IReadOnlyList<string> Globs { get => globs.Patterns; init => globs = new(value); }

    // Declared after Globs because JSON follows declaration order, and config.json lists globs before languages.
    /// <summary>Language codes the lens applies to; empty means every language.</summary>
    public IReadOnlyList<string> Languages { get; init; } = Languages;

    /// <summary>True when the lens covers a file with this path and language.</summary>
    public bool Applies(string path, string language) =>
        (Globs.Count == 0 || globs.AnyMatch(path))
        && (Languages.Count == 0 || Languages.Contains(language));

    /// <summary>Hash over every field, so any edit to the lens changes it.</summary>
    public string Hash() =>
        Hashing.Sha256Hex($"{Id}\0{Instructions}\0{string.Join('\n', Globs)}\0{string.Join('\n', Languages)}");
}
