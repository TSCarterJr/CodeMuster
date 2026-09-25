namespace CodeMuster.Mapping.TypeScript.Tests;

internal static class TestPaths
{
    private static readonly string[] Skipped = ["bin", "obj", "node_modules"];

    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The node program on PATH, found the way the CLI finds it: fully qualified directories only.</summary>
    public static Func<string> Node { get; } = () => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(Path.IsPathFullyQualified)
        .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))
        .First(File.Exists);

    public static string MixedRepo => Path.Combine(RepoRoot, "fixtures", "mixed-repo");

    public static string Golden => Path.Combine(RepoRoot, "tests", "CodeMuster.Mapping.TypeScript.Tests", "golden", "mixed-repo.json");

    public static string MixedRepoWithTypeScript()
    {
        var typescript = Path.Combine(MixedRepo, "web", "node_modules", "typescript");
        return Directory.Exists(typescript)
            ? MixedRepo
            : throw new InvalidOperationException("fixtures/mixed-repo/web/node_modules/typescript is missing; run npm ci --prefix fixtures/mixed-repo/web");
    }

    public static IReadOnlyList<string> RepoPaths(string root) =>
        [.. Files(root).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Files(string directory) =>
        Directory.GetFiles(directory).Concat(
            Directory.GetDirectories(directory).Where(child => !Skipped.Contains(Path.GetFileName(child))).SelectMany(Files));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMuster.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CodeMuster.sln not found above " + AppContext.BaseDirectory);
    }
}
