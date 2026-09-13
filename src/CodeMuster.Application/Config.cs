using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>The committed per-repo configuration (D04).</summary>
/// <param name="Lenses">Named lenses; at least one is expected.</param>
/// <param name="SliceTokenBudget">Approximate tokens of code a slice pack may show in full before farther members shrink to signatures (D07).</param>
/// <param name="ResolutionThreshold">Share of call sites, 0 to 1, the mappers must resolve before status calls slice coverage complete (D09).</param>
/// <param name="Verify">Whether every recorded finding gets a verify unit, costing about one more agent call per finding (D27, D28).</param>
/// <param name="Vulnerabilities">Whether <c>scan</c> runs each ecosystem's audit tool over the repository's manifests (D38); costs no agent calls.</param>
public sealed record Config(IReadOnlyList<Lens> Lenses, int SliceTokenBudget = 24000, double ResolutionThreshold = 0.9, bool Verify = true, bool Vulnerabilities = true)
{
    /// <summary>Repo-relative globs of files never analyzed, on top of the built-in <see cref="Exclusions"/> (D04). A glob without a slash matches file names, so a folder needs <c>folder/**</c>.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>The repository's own test command as a program and its arguments, such as <c>["dotnet", "test"]</c>. Fix mode runs it after every unit and throws away a fix that fails it (D37).</summary>
    public IReadOnlyList<string> TestCommand { get; init; } = [];

    /// <summary>True when this repository's own <see cref="Exclude"/> globs cover the path, whatever the built-in rules say about it.</summary>
    public bool ExcludedHere(string path) => Exclude.Any(glob => Glob.IsMatch(glob, path));

    /// <summary>Why a file is not analyzed: the built-in reason first, then <c>exclude:&lt;glob&gt;</c> for the first exclude glob it matches; null when it is analyzed.</summary>
    public string? ExcludedReason(string path, bool linguistGenerated) =>
        Exclusions.Reason(path, linguistGenerated)
        ?? Exclude.Where(glob => Glob.IsMatch(glob, path)).Select(glob => "exclude:" + glob).FirstOrDefault();

    /// <summary>Instructions of the lens every repo starts with.</summary>
    public const string DefaultInstructions =
        "Audit every file in this pack for defects a careful reviewer would flag: incorrect logic, unhandled failure paths, "
        + "missing authorization or tenant scoping, injection and unsafe rendering, leaked secrets, and concurrency mistakes. "
        + "Report only what the code shown evidences, cite exact lines, and prefer fewer high-confidence findings over speculation.";

    /// <summary>One lens named <c>default</c> that applies to every file.</summary>
    public static readonly Config Default = new([new Lens("default", DefaultInstructions, [], [])]);

    /// <summary>Every lens that applies to at least one of the files, ordered by id.</summary>
    public IReadOnlyList<Lens> LensesFor(IEnumerable<(string Path, string Language)> files)
    {
        var list = files.ToList();
        return Lenses
            .Where(lens => list.Any(file => lens.Applies(file.Path, file.Language)))
            .OrderBy(lens => lens.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Order-independent hash over a set of lenses; stored on each analysis so a lens edit marks its units stale.</summary>
    public static string HashOf(IEnumerable<Lens> lenses) =>
        Hashing.Sha256Hex(string.Join('\n', lenses.Select(lens => lens.Hash()).Order(StringComparer.Ordinal)));
}
