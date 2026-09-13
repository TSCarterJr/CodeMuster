namespace CodeMuster.Cli.Tests;

public class ModelOptionTests
{
    [Fact]
    public async Task RunWithAModelAndEffort_RecordsThemAndTheReportSaysWhoAudited()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3", "--model", "test-model", "--effort", "low");

        Assert.Equal(0, run.ExitCode);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("audited by fake test-model/low (", report.Stdout);
    }

    [Fact]
    public async Task RunWithoutAModel_SaysOnlyTheAgentName()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3");

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("audited by fake (", report.Stdout);
        Assert.DoesNotContain("audited by fake /", report.Stdout);
    }
}
