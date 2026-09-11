using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

internal static class Fixtures
{
    private static readonly string[] Skipped = ["bin", "obj", "node_modules"];

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMuster.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CodeMuster.sln not found above " + AppContext.BaseDirectory);
    }

    public static string Root(string name) => Path.Combine(RepoRoot(), "fixtures", name);

    public static CodeMap Golden(string name) =>
        CodeMapJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "tests", "CodeMuster.Mapping.CSharp.Tests", "golden", name + ".json")));

    public static async Task<CodeMap> MapInPlaceAsync(string name, string solution)
    {
        var root = Root(name);
        foreach (var project in Files(root).Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)))
        {
            if (!File.Exists(Path.Combine(root, Path.GetDirectoryName(project)!, "obj", "project.assets.json")))
            {
                throw new InvalidOperationException($"fixtures/{name} is not restored; run dotnet restore fixtures/{name}/{solution}");
            }
        }

        return await new RoslynMapper().MapAsync(root, IncludedPaths(root), null, CancellationToken.None);
    }

    public static IReadOnlyList<string> IncludedPaths(string root) =>
        Files(root).Where(path => Exclusions.Reason(path, linguistGenerated: false) is null).ToList();

    public static void CopyTree(string source, string target)
    {
        foreach (var path in Files(source))
        {
            var destination = Path.Combine(target, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source, path), destination);
        }
    }

    private static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.GetFiles(directory))
            {
                yield return RepoPath.Normalize(Path.GetRelativePath(root, file));
            }

            foreach (var child in Directory.GetDirectories(directory).Where(child => !Skipped.Contains(Path.GetFileName(child))))
            {
                pending.Push(child);
            }
        }
    }
}
