using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>What a spawned audit tool returned.</summary>
/// <param name="ExitCode">Its exit code; these tools use a non-zero code to mean "found something".</param>
/// <param name="Output">Everything it wrote to stdout.</param>
/// <param name="Error">Everything it wrote to stderr.</param>
public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>Runs npm, pnpm, yarn, and dotnet over the repository's manifests and turns what they say into findings-ready data (D38).</summary>
public sealed class DependencyAuditor : IDependencyAuditor
{
    /// <summary>Node lockfiles in the order a folder with several of them is audited when <c>packageManager</c> names none of them.</summary>
    private static readonly (string Lockfile, string Manager)[] NodeLockfiles =
    [
        ("pnpm-lock.yaml", "pnpm"),
        ("yarn.lock", "yarn"),
        ("npm-shrinkwrap.json", "npm"),
        ("package-lock.json", "npm"),
    ];

    private readonly Func<string, IReadOnlyList<string>, string, CancellationToken, Task<ProcessResult>> _run;

    /// <summary>Uses real processes.</summary>
    public DependencyAuditor()
        : this(RunProcessAsync)
    {
    }

    internal DependencyAuditor(Func<string, IReadOnlyList<string>, string, CancellationToken, Task<ProcessResult>> run)
    {
        _run = run;
    }

    public async Task<DependencyAudit> AuditAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var manifests = new List<ManifestVulnerabilities>();
        var diagnostics = new List<string>();
        foreach (var choice in Jobs(repoRoot, paths))
        {
            // A folder with several lockfiles tries its tools in preference order, so a preferred tool that is not installed still leaves an audit.
            Job? job = null;
            var result = new ProcessResult(0, "", "");
            var failures = new List<(string Tool, string Reason)>();
            foreach (var candidate in choice.Candidates)
            {
                progress?.Report($"auditing {candidate.Manifest} with {candidate.Executable}");
                var directory = Path.GetFullPath(Path.Combine(repoRoot, candidate.WorkingDirectory));
                try
                {
                    var attempt = candidate;
                    if (attempt.Executable == "yarn")
                    {
                        var version = await _run("yarn", ["--version"], directory, cancellationToken);
                        if (version.ExitCode != 0 || !Version.TryParse(version.Output.Trim(), out var parsed))
                            throw new InvalidOperationException("could not determine Yarn version: " + version.Output + version.Error);
                        if (parsed.Major >= 2) attempt = attempt with { Tool = "yarn npm audit", Arguments = ["npm", "audit", "--all", "--recursive", "--json"] };
                    }
                    result = await _run(attempt.Executable, attempt.Arguments, directory, cancellationToken).ConfigureAwait(false);
                    job = attempt;
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    failures.Add((candidate.Executable, ex.Message));
                }
            }

            if (choice.Warning(job?.Executable, failures) is { } warning)
            {
                diagnostics.Add(warning);
            }

            if (job is null)
            {
                continue;
            }

            if (result.Output.Trim().Length == 0 && result.ExitCode == 0 && job.Tool == "yarn npm audit")
            {
                // Yarn 2 and later report a clean audit only as an info line, which --json leaves out; its errors are JSON lines on stdout.
                manifests.Add(new ManifestVulnerabilities(job.Manifest, job.Tool, []));
                continue;
            }

            if (result.Output.Length == 0)
            {
                diagnostics.Add($"{job.Manifest}: {job.Tool} wrote nothing (exit {result.ExitCode}); {Hint(job, result)}");
                continue;
            }

            try
            {
                manifests.Add(new ManifestVulnerabilities(job.Manifest, job.Tool, job.Parse(result.Output)));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // The tool's own words beat the parser's: text on stdout (dotnet with an unreachable source) or an error on stderr (yarn classic offline).
                var reason = ex is JsonException ? AuditOutput.FirstLine(result.Output)
                    : ex.Message == AuditOutput.NoReportMessage ? AuditOutput.StderrReason(result.Error)
                    : null;
                diagnostics.Add($"{job.Manifest}: {job.Tool} gave no usable report ({reason ?? ex.Message}); any earlier findings are kept, run it by hand to see why");
            }
        }

        return new DependencyAudit(manifests, diagnostics);
    }

    private static string Hint(Job job, ProcessResult result) =>
        result.ExitCode == 127 || result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? $"install {job.Executable} or exclude that folder"
            : result.Error.Trim() is { Length: > 0 } error ? error : "run it by hand to see why";

    private static IEnumerable<Choice> Jobs(string repoRoot, IReadOnlyList<string> paths)
    {
        var normalized = paths.Select(RepoPath.Normalize).ToList();
        var known = normalized.ToHashSet(StringComparer.Ordinal);
        foreach (var manifest in normalized.Where(path => Path.GetFileName(path) == "package.json").Order(StringComparer.Ordinal))
        {
            var folder = Folder(manifest);
            var present = NodeLockfiles.Where(entry => known.Contains(folder.Length == 0 ? entry.Lockfile : folder + "/" + entry.Lockfile)).ToList();
            var managers = present.Select(entry => entry.Manager).Distinct().ToList();
            if (managers.Count == 0)
            {
                continue;
            }

            var named = managers.Count > 1 ? PackageManager(repoRoot, manifest) : null;
            var ordered = named is not null && managers.Contains(named) ? [named, .. managers.Where(manager => manager != named)] : managers;
            var candidates = ordered.Select(manager => manager switch
            {
                "pnpm" => new Job(manifest, folder, "pnpm", "pnpm audit", ["audit", "--json"], AdvisoryMapJson.Parse),
                "yarn" => new Job(manifest, folder, "yarn", "yarn audit", ["audit", "--json"], YarnAuditJson.Parse),
                _ => new Job(manifest, folder, "npm", "npm audit", ["audit", "--json"], NpmAuditJson.Parse),
            }).ToList();
            yield return new Choice(candidates, [.. present.Select(entry => entry.Lockfile)], named);
        }

        foreach (var solution in Solutions(normalized))
        {
            yield return new Choice(
                [new Job(
                    solution,
                    "",
                    "dotnet",
                    "dotnet list package",
                    ["list", solution, "package", "--vulnerable", "--include-transitive", "--format", "json"],
                    output => DotnetAuditJson.Parse(output).Select(entry => entry.Package).ToList())],
                [],
                null);
        }
    }

    private static IReadOnlyList<string> Solutions(IReadOnlyList<string> paths)
    {
        var solutions = paths
            .Where(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        return solutions.Count > 0
            ? solutions
            : paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The <c>packageManager</c> field's tool name, such as <c>pnpm</c> for <c>pnpm@9.1.0</c>, or null when the manifest cannot be read or has none.</summary>
    private static string? PackageManager(string repoRoot, string manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, manifest)));
            return AuditOutput.Text(document.RootElement, "packageManager")?.Split('@')[0].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Names(IEnumerable<string> names)
    {
        var sorted = names.Order(StringComparer.Ordinal).ToList();
        return string.Join(", ", sorted[..^1]) + " and " + sorted[^1];
    }

    private static string Folder(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo(ExecutableResolver.Resolve(executable))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{executable} did not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private sealed record Job(
        string Manifest,
        string WorkingDirectory,
        string Executable,
        string Tool,
        IReadOnlyList<string> Arguments,
        Func<string, IReadOnlyList<VulnerablePackage>> Parse);

    /// <summary>The jobs that can audit one manifest, in the order to try them; several only for a folder with lockfiles of several tools.</summary>
    private sealed record Choice(IReadOnlyList<Job> Candidates, IReadOnlyList<string> Lockfiles, string? Named)
    {
        /// <summary>What to say about the tool that ran (null when none could), or nothing for a manifest with one candidate that ran.</summary>
        public string? Warning(string? ran, IReadOnlyList<(string Tool, string Reason)> failures)
        {
            var manifest = Candidates[0].Manifest;
            if (ran is null && Candidates.Count == 1)
            {
                var (tool, reason) = failures[0];
                return $"{manifest}: {tool} could not run ({reason}); install {tool} or exclude that folder";
            }

            if (ran is null)
            {
                var reasons = string.Join("; ", failures.Select(failure => $"{failure.Tool}: {failure.Reason}"));
                return $"{manifest}: found {Names(Lockfiles)}, but no tool for them could run ({reasons}); install one of them or delete the lockfile you no longer use";
            }

            if (Candidates.Count == 1)
            {
                return null;
            }

            var why = failures.Count > 0 ? $" because {Names(failures.Select(failure => failure.Tool))} could not run"
                : ran == Named ? ", as packageManager names it"
                : ", the first of pnpm, yarn and npm";
            return $"{manifest}: found {Names(Lockfiles)}; audited with {ran}{why}; delete the lockfile you no longer use" + (ran == Named ? "" : " or set packageManager");
        }
    }
}
