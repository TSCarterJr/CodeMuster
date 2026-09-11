using System.Globalization;
using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMuster.Mapping.CSharp;

public sealed class RoslynMapper : ICodeMapper
{
    public string Language => Languages.CSharp;

    public async Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var files = WorkspaceFiles(paths);
        if (files.Count == 0)
        {
            return paths.Any(path => Languages.FromPath(path) == Languages.CSharp)
                ? throw new InvalidOperationException("C# files found but no .sln, .slnx, or .csproj is included, so there is nothing to load them with")
                : new CodeMap([], [], [], new ResolutionStats(0, 0, []), []);
        }

        var builder = new CodeMapBuilder(repoRoot, paths);
        var diagnostics = new List<string>();
        var loads = progress is null ? null : new LoadProgress(progress, repoRoot);
        if (IsSolution(files[0]))
        {
            foreach (var file in files)
            {
                progress?.Report($"loading {file}");
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(Path.Combine(repoRoot, file), loads, cancellationToken);
                await builder.AddAsync(solution, progress, cancellationToken);
                diagnostics.AddRange(Diagnostics(workspace, repoRoot, file));
            }
        }
        else
        {
            using var workspace = MSBuildWorkspace.Create();
            foreach (var file in files)
            {
                var fullPath = Path.GetFullPath(Path.Combine(repoRoot, file));
                if (!workspace.CurrentSolution.Projects.Any(project => string.Equals(project.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    progress?.Report($"loading {file}");
                    await workspace.OpenProjectAsync(fullPath, loads, cancellationToken);
                }
            }

            await builder.AddAsync(workspace.CurrentSolution, progress, cancellationToken);
            diagnostics.AddRange(Diagnostics(workspace, repoRoot, restoreTarget: null));
        }

        return builder.Build() with { Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToList() };
    }

    internal static IReadOnlyList<string> WorkspaceFiles(IReadOnlyList<string> paths)
    {
        var solutions = paths.Where(IsSolution).Order(StringComparer.Ordinal).ToList();
        return solutions.Count > 0
            ? solutions
            : paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> Diagnostics(MSBuildWorkspace workspace, string repoRoot, string? restoreTarget)
    {
        var failures = workspace.Diagnostics.Where(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure).Select(diagnostic => diagnostic.Message);
        var unrestored = workspace.CurrentSolution.Projects
            .Select(project => project.FilePath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(project => !File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json")))
            .Select(project => RepoPath.Normalize(Path.GetRelativePath(repoRoot, project)))
            .Select(project => $"{project} is not restored; run dotnet restore {restoreTarget ?? project}");
        return failures.Concat(unrestored);
    }

    private static bool IsSolution(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    private sealed class LoadProgress(IProgress<string> progress, string repoRoot) : IProgress<ProjectLoadProgress>
    {
        public void Report(ProjectLoadProgress value)
        {
            if (value.Operation == ProjectLoadOperation.Resolve)
            {
                var project = RepoPath.Normalize(Path.GetRelativePath(repoRoot, value.FilePath));
                progress.Report(string.Create(CultureInfo.InvariantCulture, $"loaded {project} in {value.ElapsedTime.TotalSeconds:0.0} s"));
            }
        }
    }
}
