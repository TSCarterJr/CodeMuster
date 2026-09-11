namespace CodeMuster.Mapping.TypeScript.Tests;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string MixedRepo => Path.Combine(RepoRoot, "fixtures", "mixed-repo");

    public static string Golden => Path.Combine(RepoRoot, "tests", "CodeMuster.Mapping.TypeScript.Tests", "golden", "mixed-repo.json");

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
