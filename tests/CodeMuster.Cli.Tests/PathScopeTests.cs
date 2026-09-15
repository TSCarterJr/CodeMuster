using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class PathScopeTests
{
    [Theory]
    [InlineData("web")]
    [InlineData("web/lib/api.ts")]
    public async Task Next_WithAPath_ReturnsOnlyUnitsTouchingThatFileOrFolder(string path)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);

        var next = await CliProcess.RunAsync(repo.Root, "next", "--path", path, "--batch", "100");

        Assert.Equal(0, next.ExitCode);
        var keys = Regex.Matches(next.Stdout, @"^- key: (.+)", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value.TrimEnd('\r')).ToList();
        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(key == path || key.StartsWith(path + "/", StringComparison.Ordinal), key));
    }

    [Fact]
    public async Task Next_WithNoPendingUnitsInPath_DoesNotFallBackToOtherFiles()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--path", "web")).ExitCode);

        var scoped = await CliProcess.RunAsync(repo.Root, "next", "--path", "web");
        var whole = await CliProcess.RunAsync(repo.Root, "next");

        Assert.Equal(0, scoped.ExitCode);
        Assert.Empty(scoped.Stdout);
        Assert.Contains("nothing pending", scoped.Stderr);
        Assert.Equal(0, whole.ExitCode);
        Assert.Contains("# CodeMuster unit", whole.Stdout);
    }

    [Fact]
    public async Task NextHelp_DescribesPathSelection()
    {
        var help = await CliProcess.RunAsync(Path.GetTempPath(), "next", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("--path <path>", help.Stdout);
        Assert.Contains("repo-relative file or folder", help.Stdout);
    }

    [Fact]
    public async Task RunAndEstimate_WithAPath_StayInsideThatFolder()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var whole = Tokens((await CliProcess.RunAsync(repo.Root, "estimate")).Stdout);
        var scoped = Tokens((await CliProcess.RunAsync(repo.Root, "estimate", "--path", "web")).Stdout);
        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3", "--path", "web");

        Assert.Equal(0, run.ExitCode);
        Assert.True(scoped < whole, $"scoped {scoped} should be under the whole {whole}");
        var units = Regex.Matches(run.Stdout, @"^\d+/\d+ (?:file|verify) (\S+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(units);
        Assert.All(units, unit => Assert.StartsWith("web/", unit));

        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.DoesNotContain("\ncomplete", status);
    }

    private static long Tokens(string estimate) =>
        long.Parse(Regex.Match(estimate, @"total ~(\d+) tokens").Groups[1].Value);
}
