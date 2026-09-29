using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class AutoCommandTests
{
    private static async Task<(TempRepo Repo, Dictionary<string, string> Environment)> PlantedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills");
        repo.WithoutVulnerabilityScan();
        var sample = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0];
        var template = Path.Combine(repo.Root, "template.json");
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [sample with { Claim = "kept claim" }])));
        return (repo, new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template });
    }

    [Fact]
    public async Task WithoutATerminal_AutoNeedsStepsSkipOrYes()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var result = await CliProcess.RunAsync(repo.Root, "auto");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("codemuster auto: no terminal to ask which steps to run; pass --steps, --skip or --yes", result.Stderr);
    }

    [Fact]
    public async Task ChosenSteps_RunInOrder_AndRunWithoutVerify_LeavesTheChecksQueued()
    {
        var (repo, environment) = await PlantedAsync();
        using var _ = repo;

        var state = Path.Combine(repo.Root, "state");
        environment["CODEMUSTER_STATE_DIR"] = state;

        var result = await CliProcess.RunAsync(repo.Root, environment, "auto", "--steps", "report,run,scan", "--agent", "fake", "-j", "2");

        Assert.Equal(0, result.ExitCode);
        var remembered = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(state, "choices"))));
        Assert.Contains("\"agent\": \"fake\"", remembered);
        Assert.Contains("\"report\"", remembered);
        var stdout = result.Stdout.ReplaceLineEndings("\n");
        Assert.True(stdout.IndexOf("== scan", StringComparison.Ordinal) < stdout.IndexOf("== run", StringComparison.Ordinal));
        Assert.True(stdout.IndexOf("== run", StringComparison.Ordinal) < stdout.IndexOf("== report", StringComparison.Ordinal));
        Assert.Contains("kept claim", stdout);
        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.Matches(@"\nverify 0/[1-9]\d*\n", status);
    }

    [Fact]
    public async Task Yes_RunsTheDefaultSteps_AndVerifiesInTheSameRun()
    {
        var (repo, environment) = await PlantedAsync();
        using var _ = repo;

        var result = await CliProcess.RunAsync(repo.Root, environment, "auto", "--yes", "--skip", "doctor", "--agent", "fake");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("== estimate", result.Stdout);
        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.Matches(@"\nverify (\d+)/\1\n", status);
    }

    [Fact]
    public async Task AnEstimateAboveMaxCost_StopsBeforeAnyAgentCall()
    {
        var (repo, environment) = await PlantedAsync();
        using var _ = repo;

        var result = await CliProcess.RunAsync(repo.Root, environment, "auto", "--steps", "scan,estimate,run", "--agent", "fake", "--max-cost", "0.0001");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("exceeds --max-cost $0.0001; nothing was run", result.Stderr);
        Assert.DoesNotContain("== run", result.Stdout);
    }

    [Fact]
    public async Task AnAgentStep_WithoutATerminal_NeedsAgent()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var result = await CliProcess.RunAsync(repo.Root, "auto", "--steps", "scan,run");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("codemuster auto: --agent is required for run, verify and fix (claude, codex, gemini, opencode)", result.Stderr);
    }

    [Fact]
    public async Task BareCodemuster_WithoutATerminal_StillPrintsHelp()
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath());

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage: codemuster", result.Stderr);
    }
}
