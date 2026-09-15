using System.Diagnostics;
using System.Text;
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
        foreach (var candidate in Jobs(paths))
        {
            var job = candidate;
            progress?.Report($"auditing {job.Manifest} with {job.Executable}");
            var directory = Path.GetFullPath(Path.Combine(repoRoot, job.WorkingDirectory));
            ProcessResult result;
            try
            {
                if (job.Executable == "yarn")
                {
                    var version = await _run("yarn", ["--version"], directory, cancellationToken);
                    if (version.ExitCode != 0 || !Version.TryParse(version.Output.Trim(), out var parsed))
                        throw new InvalidOperationException("could not determine Yarn version: " + version.Output + version.Error);
                    if (parsed.Major >= 2) job = job with { Tool = "yarn npm audit", Arguments = ["npm", "audit", "--all", "--recursive", "--json"] };
                }
                result = await _run(job.Executable, job.Arguments, directory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add($"{job.Manifest}: {job.Executable} could not run ({ex.Message}); install {job.Executable} or exclude that folder");
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
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                diagnostics.Add($"{job.Manifest}: could not read what {job.Tool} wrote ({ex.Message}); run it by hand to see why");
            }
        }

        return new DependencyAudit(manifests, diagnostics);
    }

    private static string Hint(Job job, ProcessResult result) =>
        result.ExitCode == 127 || result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? $"install {job.Executable} or exclude that folder"
            : result.Error.Trim() is { Length: > 0 } error ? error : "run it by hand to see why";

    private static IEnumerable<Job> Jobs(IReadOnlyList<string> paths)
    {
        var normalized = paths.Select(RepoPath.Normalize).ToList();
        var known = normalized.ToHashSet(StringComparer.Ordinal);
        foreach (var path in normalized.Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var folder = Folder(path);
            var manifest = folder.Length == 0 ? "package.json" : folder + "/package.json";
            switch (name)
            {
                case "package-lock.json" or "npm-shrinkwrap.json" when known.Contains(manifest):
                    yield return new Job(manifest, folder, "npm", "npm audit", ["audit", "--json"], NpmAuditJson.Parse);
                    break;
                case "pnpm-lock.yaml" when known.Contains(manifest):
                    yield return new Job(manifest, folder, "pnpm", "pnpm audit", ["audit", "--json"], AdvisoryMapJson.Parse);
                    break;
                case "yarn.lock" when known.Contains(manifest):
                    yield return new Job(manifest, folder, "yarn", "yarn audit", ["audit", "--json"], YarnAuditJson.Parse);
                    break;
            }
        }

        foreach (var solution in Solutions(normalized))
        {
            yield return new Job(
                solution,
                "",
                "dotnet",
                "dotnet list package",
                ["list", solution, "package", "--vulnerable", "--include-transitive", "--format", "json"],
                output => DotnetAuditJson.Parse(output).Select(entry => entry.Package).ToList());
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
}
