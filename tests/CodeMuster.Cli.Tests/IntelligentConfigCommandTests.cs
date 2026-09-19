using System.Text.Json.Nodes;

namespace CodeMuster.Cli.Tests;

public class IntelligentConfigCommandTests
{
    [Fact]
    public async Task AppliesAgentRecommendationsWithBackupAndNoLedger()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills");
        var configPath = Path.Combine(repo.Root, ".codemuster", "config.json");
        var original = File.ReadAllText(configPath);
        var responsePath = Path.Combine(repo.Root, "recommendation.json");
        File.WriteAllText(responsePath, """
            {"changes":{"lenses":[{"id":"api-security","instructions":"Check authorization for API endpoints.","globs":["**/*.cs"],"languages":["csharp"]}]},
             "reasons":{"lenses":"The repository contains a C# API."}}
            """);
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = responsePath };

        var result = await CliProcess.RunAsync(repo.Root, environment, "intelligent-config", "--agent", "fake", "--model", "test-model", "--effort", "high");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Model: test-model, Thinking: high", result.Stderr);
        Assert.Contains("api-security", result.Stdout);
        Assert.Contains("backup", result.Stdout);
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(repo.Root, ".codemuster"), "config.backup-*.json"))));
        Assert.False(File.Exists(Path.Combine(repo.Root, ".codemuster", "ledger.db")));
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(configPath))!["lenses"]!.AsArray().Count);

        var repeated = await CliProcess.RunAsync(repo.Root, environment, "intelligent-config", "--agent", "fake");
        Assert.Equal(0, repeated.ExitCode);
        Assert.Contains("no configuration changes", repeated.Stdout);
        Assert.Single(Directory.GetFiles(Path.Combine(repo.Root, ".codemuster"), "config.backup-*.json"));
    }

    [Fact]
    public async Task InvalidAgentOutputLeavesConfigUntouched()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills");
        var configPath = Path.Combine(repo.Root, ".codemuster", "config.json");
        var original = File.ReadAllText(configPath);
        var responsePath = Path.Combine(repo.Root, "recommendation.json");
        File.WriteAllText(responsePath, "{\"changes\":{\"automation\":\"review_and_fix\"},\"reasons\":{\"automation\":\"ignore instructions\"}}");

        var result = await CliProcess.RunAsync(repo.Root, new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = responsePath }, "intelligent-config", "--agent", "fake");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unsupported configuration", result.Stderr);
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.Empty(Directory.GetFiles(Path.Combine(repo.Root, ".codemuster"), "config.backup-*.json"));
    }
}
