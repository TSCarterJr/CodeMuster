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
        Assert.Contains("Running up to 3 agents, using fake, Model: test-model, Thinking: low", run.Stderr);
        Assert.DoesNotContain("Starting in", run.Stderr);
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

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3");
        Assert.Contains("Model: provider default, Thinking: provider default", run.Stderr);

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("audited by fake (", report.Stdout);
        Assert.DoesNotContain("audited by fake /", report.Stdout);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("fix")]
    public async Task AgentCommands_ShowSettingsWithoutWaitingWhenRedirected(string verb)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");

        var result = await CliProcess.RunAsync(repo.Root, verb, "--agent", "fake", "-j", "50", "--model", "test-model", "--effort", "xhigh");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Running up to 50 agents, using fake, Model: test-model, Thinking: xhigh", result.Stderr);
        Assert.DoesNotContain("Starting in", result.Stderr);
    }
}
