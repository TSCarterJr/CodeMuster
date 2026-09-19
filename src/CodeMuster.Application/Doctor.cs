using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Checks that this machine can map the repository with functional probes, never liveness checks (D09): git lists the tracked files, then every mapper whose language has an included file maps the repository for real. A failure carries the fix to run; doctor never runs it. Without a <paramref name="config"/>, as before <c>init</c>, only the built-in exclusions apply. Each step is reported to <paramref name="progress"/> as it happens, under <c>git</c> or the mapper's language.</summary>
public sealed class Doctor(ISourceTree tree, IReadOnlyList<ICodeMapper> mappers, IClock clock, string repoRoot, Config? config = null, IProgress<string>? progress = null)
{
    /// <summary>Runs git, then each mapper in order, and reports what each one needs.</summary>
    public async Task<DoctorReport> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceFile> files;
        try
        {
            files = await tree.ListFilesAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return DoctorReport.GitFailed(ex.Message);
        }

        progress?.Report($"git: listed {files.Count} files");
        var settings = config ?? Config.Default;
        var paths = files.Where(f => settings.IsMappingInput(f.Path, settings.ExcludedReason(f.Path, f.LinguistGenerated))).Select(f => f.Path).ToList();
        var probes = new List<DoctorProbe> { new("git", ProbeState.Working, null, 0, []) };
        foreach (var mapper in mappers.Where(m => paths.Any(path => Languages.FromPath(path) == m.Language)))
        {
            probes.Add(await ProbeAsync(mapper, paths, cancellationToken));
        }

        return new DoctorReport(probes);
    }

    private async Task<DoctorProbe> ProbeAsync(ICodeMapper mapper, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        try
        {
            var map = await mapper.MapAsync(repoRoot, paths, PrefixedProgress.For(progress, mapper.Language), cancellationToken);
            var elapsed = clock.UtcNow - started;
            return map.Diagnostics.Count > 0 ? new DoctorProbe(mapper.Language, ProbeState.Failed, elapsed, map.Symbols.Count, map.Diagnostics)
                : map.Symbols.Count == 0 ? new DoctorProbe(mapper.Language, ProbeState.LoadedButEmpty, elapsed, 0, [EmptyHint(mapper.Language)])
                : new DoctorProbe(mapper.Language, ProbeState.Working, elapsed, map.Symbols.Count, []);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new DoctorProbe(mapper.Language, ProbeState.Failed, clock.UtcNow - started, 0, [ex.Message]);
        }
    }

    private static string EmptyHint(string language) => language == Languages.TypeScript
        ? "no symbols came back; check that a tracked tsconfig.json includes the TypeScript files"
        : "no symbols came back; check that the solution builds with dotnet build";
}
