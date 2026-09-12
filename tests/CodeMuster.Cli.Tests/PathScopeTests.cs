using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class PathScopeTests
{
    [Fact]
    public async Task RunAndEstimate_WithAPath_StayInsideThatFolder()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
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
