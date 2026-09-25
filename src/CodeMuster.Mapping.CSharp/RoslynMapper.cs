using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMuster.Mapping.CSharp;

public sealed class RoslynMapper : ICodeMapper
{
    public string Language => Languages.CSharp;

    public async Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        // MSBuildWorkspace starts its build host by the bare name dotnet, and .NET on Linux and macOS looks for that in the current
        // directory before PATH, so the repository being mapped could supply it. Every path below is absolute.
        var root = Path.GetFullPath(repoRoot);
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        try
        {
            return await MapFromBaseDirectoryAsync(root, paths, progress, cancellationToken);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    private static async Task<CodeMap> MapFromBaseDirectoryAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
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
        var loadedProjects = new HashSet<string>(ProjectPathComparer);
        if (IsSolution(files[0]))
        {
            foreach (var file in files)
            {
                progress?.Report($"loading {file}");
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(Path.Combine(repoRoot, file), loads, cancellationToken);
                await builder.AddAsync(solution, progress, cancellationToken);
                diagnostics.AddRange(Diagnostics(workspace, repoRoot, file));
                loadedProjects.UnionWith(solution.Projects.Select(project => project.FilePath).OfType<string>().Select(Path.GetFullPath));
            }
        }

        var projects = paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => !loadedProjects.Contains(Path.GetFullPath(Path.Combine(repoRoot, path))))
            .Order(StringComparer.Ordinal).ToList();
        if (projects.Count > 0)
        {
            using var workspace = MSBuildWorkspace.Create();
            foreach (var file in projects)
            {
                var fullPath = Path.GetFullPath(Path.Combine(repoRoot, file));
                if (!loadedProjects.Contains(fullPath))
                {
                    progress?.Report($"loading {file}");
                    await workspace.OpenProjectAsync(fullPath, loads, cancellationToken);
                    loadedProjects.UnionWith(workspace.CurrentSolution.Projects.Select(project => project.FilePath).OfType<string>().Select(Path.GetFullPath));
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
            .Distinct(ProjectPathComparer)
            .Where(project => !File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json")))
            .Select(project => RepoPath.Normalize(Path.GetRelativePath(repoRoot, project)))
            .Select(project => $"{project} is not restored; run dotnet restore {restoreTarget ?? project}");
        return failures.Concat(unrestored);
    }

    private static bool IsSolution(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    private static StringComparer ProjectPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class LoadProgress(IProgress<string> progress, string repoRoot) : IProgress<ProjectLoadProgress>
    {
        public void Report(ProjectLoadProgress value)
        {
            if (value.Operation == ProjectLoadOperation.Resolve)
            {
                progress.Report($"loaded {RepoPath.Normalize(Path.GetRelativePath(repoRoot, value.FilePath))}");
            }
        }
    }
}
