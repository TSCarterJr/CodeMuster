using CodeMuster.Domain;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMuster.Mapping.CSharp;

public sealed class RoslynMapper : ICodeMapper
{
    public string Language => Languages.CSharp;

    public async Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var files = WorkspaceFiles(paths);
        if (files.Count == 0)
        {
            return paths.Any(path => Languages.FromPath(path) == Languages.CSharp)
                ? throw new InvalidOperationException("C# files found but no .sln, .slnx, or .csproj is included, so there is nothing to load them with")
                : new CodeMap([], [], [], new ResolutionStats(0, 0, []), []);
        }

        var builder = new CodeMapBuilder(repoRoot, paths);
        if (IsSolution(files[0]))
        {
            foreach (var file in files)
            {
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(Path.Combine(repoRoot, file), cancellationToken: cancellationToken);
                await builder.AddAsync(solution, cancellationToken);
            }
        }
        else
        {
            using var workspace = MSBuildWorkspace.Create();
            foreach (var file in files.Select(file => Path.GetFullPath(Path.Combine(repoRoot, file))))
            {
                if (!workspace.CurrentSolution.Projects.Any(project => string.Equals(project.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                {
                    await workspace.OpenProjectAsync(file, cancellationToken: cancellationToken);
                }
            }

            await builder.AddAsync(workspace.CurrentSolution, cancellationToken);
        }

        return builder.Build();
    }

    internal static IReadOnlyList<string> WorkspaceFiles(IReadOnlyList<string> paths)
    {
        var solutions = paths.Where(IsSolution).Order(StringComparer.Ordinal).ToList();
        return solutions.Count > 0
            ? solutions
            : paths.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
    }

    private static bool IsSolution(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}
