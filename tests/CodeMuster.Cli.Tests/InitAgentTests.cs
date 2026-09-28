using System.Text.Json.Nodes;

namespace CodeMuster.Cli.Tests;

public class InitAgentTests
{
    [Fact]
    public async Task SelectedAgents_InstallSkillsAndHooks_PreserveSettings_AndRepeatSafely()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var settings = Path.Combine(repo.Root, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        await File.WriteAllTextAsync(settings, "{\"permissions\":{\"deny\":[\"secret\"]},\"hooks\":{\"PostToolUse\":[{\"matcher\":\"Edit\",\"hooks\":[{\"type\":\"command\",\"command\":\"existing-hook\"}]}]}}");
        var first = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for", "claude,codex");
        Assert.Equal(0, first.ExitCode);
        var installed = await File.ReadAllTextAsync(settings);
        Assert.Contains("existing-hook", installed);
        Assert.Contains("secret", installed);
        Assert.Contains("codemuster hook", installed);
        Assert.True(File.Exists(Path.Combine(repo.Root, ".codex", "hooks.json")));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".codex", "skills", "codemuster", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".gemini")));
        var lines = first.Stdout.ReplaceLineEndings("\n");
        Assert.Contains("installed claude skill; added its change hook to .claude/settings.json, which rewrites that file without its comments (reload the agent and approve hooks if prompted)\n", lines);
        Assert.Contains("installed codex skill; added its change hook to .codex/hooks.json (reload the agent and approve hooks if prompted)\n", lines);

        var again = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for", "claude,codex");

        Assert.Equal(0, again.ExitCode);
        Assert.Equal(installed, await File.ReadAllTextAsync(settings));
        Assert.NotNull(JsonNode.Parse(installed));
        Assert.StartsWith("claude skill and change hook already current\ncodex skill and change hook already current\n", again.Stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task OptOut_LeavesGitignoreAndHooksAlone_ButInstallsSelectedSkill()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var ignore = File.Exists(Path.Combine(repo.Root, ".gitignore")) ? File.ReadAllText(Path.Combine(repo.Root, ".gitignore")) : "";
        var result = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-gitignore", "--no-hooks", "--no-mcp", "--for", "gemini");
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(repo.Root, ".gemini", "skills", "codemuster", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(repo.Root, ".gemini", "settings.json")));
        Assert.Equal(ignore, File.Exists(Path.Combine(repo.Root, ".gitignore")) ? File.ReadAllText(Path.Combine(repo.Root, ".gitignore")) : "");
    }

    [Fact]
    public async Task SelectedAgents_GetTheMcpServer_TheWayThePluginRegistersIt_UnlessOptedOut()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var plugin = JsonNode.Parse(File.ReadAllText(Path.Combine(TempRepo.FindRepoRoot(), "distribution", "mcp.json")))!;

        var first = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for", "claude,codex,gemini");
        var again = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for", "claude,codex,gemini");

        Assert.Equal(0, first.ExitCode);
        Assert.True(JsonNode.DeepEquals(plugin, JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, ".mcp.json")))));
        Assert.True(JsonNode.DeepEquals(plugin["mcpServers"]!["codemuster"], JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, ".gemini", "settings.json")))!["mcpServers"]!["codemuster"]));
        Assert.StartsWith("[mcp_servers.codemuster]\ncommand = \"node\"\n", File.ReadAllText(Path.Combine(repo.Root, ".codex", "config.toml")));
        var lines = first.Stdout.ReplaceLineEndings("\n");
        Assert.Contains("added the codemuster MCP server to .mcp.json for claude (approve it when the agent asks)\n", lines);
        Assert.Contains("added the codemuster MCP server to .codex/config.toml for codex (approve it when the agent asks)\n", lines);
        Assert.Contains("codemuster MCP server already in .mcp.json for claude\n", again.Stdout.ReplaceLineEndings("\n"));

        using var optOut = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(optOut.Root, "init", "--yes", "--no-mcp", "--for", "claude")).ExitCode);
        Assert.False(File.Exists(Path.Combine(optOut.Root, ".mcp.json")));
    }

    [Fact]
    public async Task Hook_NotifiesWithoutTouchingTrackedFiles_AndScanAcknowledgesIt()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills");
        repo.WithoutVulnerabilityScan();
        var before = repo.Git("status", "--porcelain");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "hook")).ExitCode);
        Assert.Equal(before, repo.Git("status", "--porcelain"));
        Assert.Contains("files changed since the last scan; run codemuster scan to refresh coverage", (await CliProcess.RunAsync(repo.Root, "status")).Stderr);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        Assert.DoesNotContain("since the last scan", (await CliProcess.RunAsync(repo.Root, "status")).Stderr);
    }
}
