using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Checks that this machine can map the repository with functional probes, never liveness checks (D09): git lists the tracked files, then every mapper whose language has an included file maps the repository for real. A failure carries the fix to run, and the report lists the commands that fix what doctor recognizes, which only <see cref="DoctorFixer"/> runs (D70). Without a <paramref name="config"/>, as before <c>init</c>, only the built-in exclusions apply. Each step is reported to <paramref name="progress"/> as it happens, under <c>git</c> or the mapper's language. <paramref name="commands"/> answers what the probes cannot, such as whether HEAD exists, a .NET SDK is installed, or pnpm and yarn are on PATH; without it those questions are not asked.</summary>
public sealed class Doctor(ISourceTree tree, IReadOnlyList<ICodeMapper> mappers, IClock clock, string repoRoot, Config? config = null, IProgress<string>? progress = null, ICommandRunner? commands = null)
{
    /// <summary>What doctor says when the C# mapper fails and no .NET SDK is installed.</summary>
    public const string SdkMissing = "the C# mapper needs the .NET SDK, which was not found; install it from https://dotnet.microsoft.com/download";

    // The two mapper diagnostics whose fix doctor can run: RoslynMapper's unrestored project and map.js's missing typescript package.
    private static readonly Regex Unrestored = new(@"is not restored; run dotnet restore (?<target>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex TypeScriptMissing = new(@"typescript was not found for (?<tsconfig>[^;]+);", RegexOptions.CultureInvariant);

    // With no tsconfig or jsconfig, map.js maps a default program and names where typescript should be added as a dev dependency (D64).
    private static readonly Regex DefaultProgramHint = new(
        @"typescript was not found for the JavaScript and TypeScript files; (?:run npm i -D typescript(?: --prefix (?<npm>\S+))?$|add typescript as a dev dependency of (?:the repository|(?<other>.+?)) with its package manager)",
        RegexOptions.CultureInvariant);

    /// <summary>Runs git, then each mapper in order, and reports what each one needs.</summary>
    public async Task<DoctorReport> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceFile> files;
        try
        {
            files = await tree.ListFilesAsync(cancellationToken);
            // Listing reads no history (D75), so a repository without a commit is found here.
            await tree.HeadCommitAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var failed = DoctorReport.GitFailed(ex.Message);
            return await HasNoCommitAsync(cancellationToken) ? failed with { Fixes = [DoctorReport.FirstCommit(init: false)] } : failed;
        }

        progress?.Report($"git: listed {files.Count} files");
        var settings = config ?? Config.Default;
        var paths = files.Where(f => settings.IsMappingInput(f.Path, settings.ExcludedReason(f.Path, f.LinguistGenerated))).Select(f => f.Path).ToList();
        var probes = new List<DoctorProbe> { new("git", ProbeState.Working, null, 0, []) };
        foreach (var mapper in mappers.Where(m => paths.Any(path => m.Languages.Contains(Languages.FromPath(path)))))
        {
            probes.Add(await ProbeAsync(mapper, paths, cancellationToken));
        }

        return new DoctorReport(probes) { Fixes = Fixes(probes, files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal)) };
    }

    private async Task<DoctorProbe> ProbeAsync(ICodeMapper mapper, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var probe = await MapAsync(mapper, paths, cancellationToken);
        return probe.State == ProbeState.Failed && mapper.Language == Languages.CSharp && await SdkMissingAsync(cancellationToken)
            ? probe with { Problems = [SdkMissing] }
            : probe;
    }

    private async Task<DoctorProbe> MapAsync(ICodeMapper mapper, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        try
        {
            var map = await mapper.MapAsync(repoRoot, paths, PrefixedProgress.For(progress, mapper.Language), cancellationToken);
            var elapsed = clock.UtcNow - started;
            if (map.SkippedLanguages.Count > 0 && map.Symbols.Count == 0 && map.Diagnostics.Count == 0)
            {
                return new DoctorProbe(map.SkippedLanguages[0].Language, ProbeState.NotMapped, elapsed, 0, [.. map.SkippedLanguages.Select(skipped => skipped.Note)]);
            }

            return map.Diagnostics.Count > 0 ? new DoctorProbe(mapper.Language, ProbeState.Failed, elapsed, map.Symbols.Count, map.Diagnostics)
                : map.Symbols.Count == 0 ? new DoctorProbe(mapper.Language, ProbeState.LoadedButEmpty, elapsed, 0, [EmptyHint(mapper.Language)])
                : new DoctorProbe(mapper.Language, ProbeState.Working, elapsed, map.Symbols.Count, []);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new DoctorProbe(mapper.Language, ProbeState.Failed, clock.UtcNow - started, 0, [ex.Message]);
        }
    }

    // Exit 1 is git's answer for a repository whose branch has no commit yet; anything else, such as dubious ownership, is not fixed by committing.
    private async Task<bool> HasNoCommitAsync(CancellationToken cancellationToken) =>
        commands is not null && (await commands.RunAsync(repoRoot, ["git", "rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)).ExitCode == 1;

    private async Task<bool> SdkMissingAsync(CancellationToken cancellationToken)
    {
        if (commands is null)
        {
            return false;
        }

        if (!commands.IsOnPath("dotnet"))
        {
            return true;
        }

        var sdks = await commands.RunAsync(repoRoot, ["dotnet", "--list-sdks"], cancellationToken);
        return !sdks.Succeeded || sdks.Output.Trim().Length == 0;
    }

    private List<DoctorFix> Fixes(IReadOnlyList<DoctorProbe> probes, IReadOnlySet<string> tracked)
    {
        var restores = Matches(probes, Languages.CSharp, Unrestored, "target")
            .Select(target => new DoctorFix("", [["dotnet", "restore", target]]));
        var installs = Matches(probes, Languages.TypeScript, TypeScriptMissing, "tsconfig")
            .Where(tsconfig => tsconfig.EndsWith(".json", StringComparison.Ordinal))
            .Select(tsconfig => PackageFolder(Folder(tsconfig), tracked))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(folder => new DoctorFix(folder, [Install(folder, tracked)]));
        var devDependencies = probes.Where(probe => probe.Name == Languages.TypeScript)
            .SelectMany(probe => probe.Problems)
            .Select(problem => DefaultProgramHint.Match(problem))
            .Where(match => match.Success)
            .Select(match => match.Groups["npm"].Success
                ? new DoctorFix(match.Groups["npm"].Value, [["npm", "i", "-D", "typescript"]])
                : new DoctorFix(match.Groups["other"].Value, [AddTypeScript(match.Groups["other"].Value, tracked)]))
            .DistinctBy(fix => fix.Render(), StringComparer.Ordinal);
        return [.. restores, .. installs, .. devDependencies];
    }

    private static IEnumerable<string> Matches(IReadOnlyList<DoctorProbe> probes, string language, Regex pattern, string group) =>
        probes.Where(probe => probe.Name == language)
            .SelectMany(probe => probe.Problems)
            .Select(problem => pattern.Match(problem))
            .Where(match => match.Success)
            .Select(match => match.Groups[group].Value)
            .Distinct(StringComparer.Ordinal);

    /// <summary>The nearest folder at or above <paramref name="folder"/> with a tracked package.json, or null when there is none.</summary>
    private static string? PackageFolder(string folder, IReadOnlySet<string> tracked)
    {
        while (true)
        {
            if (tracked.Contains(In(folder, "package.json")))
            {
                return folder;
            }

            if (folder.Length == 0)
            {
                return null;
            }

            folder = Folder(folder);
        }
    }

    private string[] Install(string folder, IReadOnlySet<string> tracked) =>
        tracked.Contains(In(folder, "package-lock.json")) || tracked.Contains(In(folder, "npm-shrinkwrap.json")) ? ["npm", "ci"]
        : tracked.Contains(In(folder, "pnpm-lock.yaml")) && commands?.IsOnPath("pnpm") == true ? ["pnpm", "install"]
        : tracked.Contains(In(folder, "yarn.lock")) && commands?.IsOnPath("yarn") == true ? ["yarn", "install"]
        : ["npm", "install"];

    private string[] AddTypeScript(string folder, IReadOnlySet<string> tracked) =>
        tracked.Contains(In(folder, "pnpm-lock.yaml")) && commands?.IsOnPath("pnpm") == true ? ["pnpm", "add", "-D", "typescript"]
        : tracked.Contains(In(folder, "yarn.lock")) && commands?.IsOnPath("yarn") == true ? ["yarn", "add", "-D", "typescript"]
        : ["npm", "i", "-D", "typescript"];

    private static string In(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    private static string Folder(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static string EmptyHint(string language) => language == Languages.TypeScript
        ? "no symbols came back; check that a tracked tsconfig.json or jsconfig.json includes the TypeScript and JavaScript files"
        : "no symbols came back; check that the solution builds with dotnet build";
}
