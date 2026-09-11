using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

internal static class Fixtures
{
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
}
