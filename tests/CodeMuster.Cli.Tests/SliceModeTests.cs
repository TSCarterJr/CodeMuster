using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class SliceModeTests
{
    [Fact]
    public async Task Scan_BuildsFlowsFromTheFixture_AndTheFakeAgentAuditsEveryUnit()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");

        var scan = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, scan.ExitCode);
        Assert.Equal("", scan.Stderr);
        var counts = Regex.Match(scan.Stdout, @"^(\d+) slices, (\d+) orphans, (\d+) files, resolution 100\.0%\r?$", RegexOptions.Multiline);
        Assert.True(counts.Success, scan.Stdout);
        Assert.Equal("2", counts.Groups[1].Value);

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "4");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("slice:web/app/quotes/page.tsx#QuotesPage", run.Stdout);

        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains("\nslice 2/2\n", status.Stdout);
        Assert.EndsWith("\ncomplete", status.Stdout.TrimEnd());

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("| /quotes | done | fake analysis |", report.Stdout);
        Assert.Contains("| /customers | done | fake analysis |", report.Stdout);
    }
}
