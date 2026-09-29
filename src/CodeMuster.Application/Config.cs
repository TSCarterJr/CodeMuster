using System.Text.Json.Serialization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>The committed per-repo configuration (D04).</summary>
/// <param name="Lenses">Named lenses; at least one is expected.</param>
/// <param name="SliceTokenBudget">Approximate tokens of code shown in full: oversized whole-file packs are skipped, and farther slice members shrink to signatures (D07).</param>
/// <param name="ResolutionThreshold">Share of call sites, 0 to 1, the mappers must resolve before status calls slice coverage complete (D09).</param>
/// <param name="Verify">Whether every recorded finding gets a verify unit, costing about one more agent call per finding (D27, D28).</param>
/// <param name="Vulnerabilities">Whether <c>scan</c> runs each ecosystem's audit tool over the repository's manifests (D38); costs no agent calls.</param>
public sealed record Config(IReadOnlyList<Lens> Lenses, int SliceTokenBudget = 24000, double ResolutionThreshold = 0.9, bool Verify = true, bool Vulnerabilities = true)
{
    private readonly GlobList excludeGlobs = new([]);

    /// <summary>Repo-relative globs of files never analyzed, on top of the built-in <see cref="Exclusions"/> (D04). A glob without a slash matches file names, so a folder needs <c>folder/**</c>.</summary>
    public IReadOnlyList<string> Exclude { get => excludeGlobs.Patterns; init => excludeGlobs = new(value); }

    /// <summary>The repository's own test command as a program and its arguments, such as <c>["dotnet", "test"]</c>. Fix mode runs it after every unit and throws away a fix that fails it (D37).</summary>
    public IReadOnlyList<string> TestCommand { get; init; } = [];

    /// <summary>The proactive work agents may perform; explicit CLI commands remain available in every mode.</summary>
    public AutomationMode Automation { get; init; } = AutomationMode.Update;

    /// <summary>Whether scan reports conservative unused-code candidates from supported call maps.</summary>
    public bool DeadCode { get; init; }

    /// <summary>Whether scan plans an <c>impact</c> unit for each symbol whose body or signature changed since the stored map (D67).</summary>
    public bool Impact { get; init; } = true;

    /// <summary>Whether scan plans a <c>duplicate</c> unit for each group of symbols with the same normalized body hash (D68).</summary>
    public bool Duplicates { get; init; } = true;

    /// <summary>Whether scan plans the <c>architecture</c> unit over the UI's structure and the <c>api</c> unit over the endpoint map (D69).</summary>
    public bool ArchitectureReview { get; init; } = true;

    /// <summary>Most findings of one analysis verified in one agent call (D78); 1 gives every finding its own verify unit (D27).</summary>
    public int VerifyBatch { get; init; } = 6;

    /// <summary>Most small units <c>run</c> reviews in one agent call (D80): units whose pack is at most a quarter of <see cref="SliceTokenBudget"/>, with the same lenses and the same directories; 1 turns batching off.</summary>
    public int BatchUnits { get; init; } = 4;

    /// <summary>Whether test files are reviewed like any other file. Off by default: test files are recorded excluded with reason <c>test</c> but still mapped (D79).</summary>
    public bool ReviewTests { get; init; }

    /// <summary>Per-model prices that override or extend the bundled price table (D63); null when the repository sets none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ModelPrice>? Prices { get; init; }

    /// <summary>Optional browser-based reviews, scoped to UI files.</summary>
    public UserExperienceSettings UserExperience { get; init; } = new();

    /// <summary>True when this repository's own <see cref="Exclude"/> globs cover the path, whatever the built-in rules say about it.</summary>
    public bool ExcludedHere(string path) => excludeGlobs.AnyMatch(path);

    /// <summary>Why a file is not analyzed: the built-in reason first, then <c>exclude:&lt;glob&gt;</c> for the first exclude glob it matches, then <c>test</c> for test code unless <see cref="ReviewTests"/> is on (D79); null when it is analyzed.</summary>
    /// <param name="path">Repo-relative path.</param>
    /// <param name="linguistGenerated">Whether gitattributes mark the file generated.</param>
    /// <param name="inTestProject">Whether the file belongs to a C# test project, which only scan reads project files to know.</param>
    public string? ExcludedReason(string path, bool linguistGenerated, bool inTestProject = false) =>
        Exclusions.Reason(path, linguistGenerated)
        ?? (excludeGlobs.FirstMatch(path) is { } glob ? "exclude:" + glob : null)
        ?? (!ReviewTests && (inTestProject || TestFiles.IsTestPath(path)) ? TestReason : null);

    /// <summary>The exclusion reason of test code (D79).</summary>
    public const string TestReason = "test";

    /// <summary>True when an edit to the file changes what the next scan plans or records: a file it reviews, a solution, project, tsconfig.json or jsconfig.json the mappers load, or, with <see cref="Vulnerabilities"/> on, a package.json whose audit unit it fingerprints (D38). Lockfiles and other excluded files do not count.</summary>
    public bool AffectsScan(string path, bool linguistGenerated)
    {
        var reason = ExcludedReason(path, linguistGenerated);
        return IsMappingInput(path, reason)
            || Vulnerabilities && reason == "data" && !ExcludedHere(path) && Path.GetFileName(path) == "package.json";
    }

    internal bool IsMappingInput(string path, string? excludedReason) =>
        excludedReason is null or TestReason || excludedReason == "data" && !ExcludedHere(path)
        && (Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj"
            || Path.GetFileName(path) is "tsconfig.json" or "jsconfig.json");

    /// <summary>Instructions of the lens every repo starts with.</summary>
    public const string DefaultInstructions =
        "Audit every file in this pack for defects a careful reviewer would flag: incorrect logic, unhandled failure paths, "
        + "missing authorization or tenant scoping, injection and unsafe rendering, leaked secrets, and concurrency mistakes. "
        + "Report only what the code shown evidences, cite exact lines, and prefer fewer high-confidence findings over speculation.";

    /// <summary>One lens named <c>default</c> that applies to every file.</summary>
    public static readonly Config Default = new([new Lens("default", DefaultInstructions, [], [])]);

    /// <summary>The category the <see cref="Simplify"/> lens asks for, reported apart from defects and fixed only on request (D68).</summary>
    public const string SimplificationCategory = "simplification";

    /// <summary>The built-in lens that finds code which could be simpler (D68); new configurations carry it after the default lens.</summary>
    public static readonly Lens Simplify = new(
        "simplify",
        "Look only for code that could be simpler without changing what it does. Flag comments that restate the code next to them "
        + "(such as `string name; // the user's name`), commented-out code, stale comments that contradict the code, and needless complexity: "
        + "redundant conditionals, re-implementations of standard library calls, and wrappers that add nothing. "
        + "Never flag a comment that explains why the code is the way it is. "
        + "Report each finding with category \"simplification\" and severity \"low\", cite its exact lines, and say what the simpler form is.",
        [],
        []);

    /// <summary>What <c>init</c> writes for a repository with no configuration: the default lens, then <see cref="Simplify"/>. Existing configurations are never changed, because a new lens re-audits every unit.</summary>
    public static readonly Config NewRepository = Default with { Lenses = [.. Default.Lenses, Simplify] };

    /// <summary>True for a finding the <see cref="Simplify"/> lens reports, whatever its case or surrounding spaces.</summary>
    public static bool IsSimplification(Finding finding) =>
        string.Equals(finding.Category.Trim(), SimplificationCategory, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every lens that applies to at least one of the files, ordered by id.</summary>
    public IReadOnlyList<Lens> LensesFor(IEnumerable<(string Path, string Language)> files)
    {
        var list = files.ToList();
        var selected = Lenses
            .Where(lens => list.Any(file => lens.Applies(file.Path, file.Language)))
            .ToList();
        if (DeadCode) selected.Add(new Lens(DeadCodeReview.Id, DeadCodeReview.Instructions, [], []));
        if (list.Any(file => UserExperience.Applies(file.Path))) selected.Add(UxReview.Lens(UserExperience));
        return selected
            .OrderBy(lens => lens.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Order-independent hash over a set of lenses; stored on each analysis so a lens edit marks its units stale.</summary>
    public static string HashOf(IEnumerable<Lens> lenses) =>
        Hashing.Sha256Hex(string.Join('\n', lenses.Select(lens => lens.Hash()).Order(StringComparer.Ordinal)));
}
