using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class DoctorCommandTests
{
    [Fact]
    public async Task Doctor_IsReadyOnARestoredFixture_AndNamesTheRestoreOnceObjIsGone()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");

        var healthy = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.Equal(0, healthy.ExitCode);
        var lines = healthy.Stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("git: working", lines[0]);
        Assert.Matches(@"^csharp: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", lines[1]);
        Assert.Matches(@"^typescript: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", lines[2]);
        Assert.Equal("ready", lines[3]);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".codemuster")));
        var progress = healthy.Stderr.ReplaceLineEndings("\n");
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] git: listed \d+ files$", RegexOptions.Multiline), progress);
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] csharp: loading MixedRepo\.sln$", RegexOptions.Multiline), progress);
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] typescript: loading web/tsconfig\.json$", RegexOptions.Multiline), progress);

        foreach (var obj in Directory.GetDirectories(Path.Combine(repo.Root, "src"), "obj", SearchOption.AllDirectories))
        {
            Directory.Delete(obj, recursive: true);
        }

        var unrestored = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.Equal(1, unrestored.ExitCode);
        var output = unrestored.Stdout.ReplaceLineEndings("\n");
        Assert.Matches(new Regex(@"^csharp: failed in \d+\.\d s$", RegexOptions.Multiline), output);
        Assert.Contains("  src/MixedRepo.Api/MixedRepo.Api.csproj is not restored; run dotnet restore MixedRepo.sln\n", output);
        Assert.EndsWith("\nnot ready", output.TrimEnd());
    }

    [Fact]
    public async Task Doctor_OutsideAGitRepository_SaysGitFailed_AndExits1()
    {
        var folder = Directory.CreateTempSubdirectory("codemuster-doctor-");
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_CEILING_DIRECTORIES"] = folder.Parent!.FullName };

            var result = await CliProcess.RunAsync(folder.FullName, environment, "doctor");

            Assert.Equal(1, result.ExitCode);
            var output = result.Stdout.ReplaceLineEndings("\n");
            Assert.StartsWith("git: failed\n  ", output);
            Assert.Contains("not a git repository", output);
            Assert.EndsWith("\nnot ready", output.TrimEnd());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
