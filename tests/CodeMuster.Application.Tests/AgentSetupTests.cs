using System.Text.Json.Nodes;
using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class AgentSetupTests
{
    private const string Root = "/repo";
    private const string OldMatcher = "Edit|Write|apply_patch|Bash|NotebookEdit";

    private readonly FakeFileSystem fileSystem = new();

    private Task InstallAsync(params string[] agents) =>
        new AgentSetup(fileSystem).InstallAsync(Root, agents, hooks: true, "skill", CancellationToken.None);

    private static string SettingsPath(string agent) => Path.Combine(Root, "." + agent, agent == "codex" ? "hooks.json" : "settings.json");

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task The_change_hook_matches_the_same_tools_as_the_plugin_hooks(string agent)
    {
        await InstallAsync(agent);

        Assert.Equal(PluginTools(), Tools(CodeMusterEntry(agent)["matcher"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_rerun_repairs_an_outdated_codemuster_matcher_and_timeout_in_place_and_leaves_other_hooks_alone()
    {
        fileSystem.Files[Path.Combine(Root, ".claude", "settings.json")] = $$$"""
            {"hooks":{"PostToolUse":[
              {"matcher":"Edit","hooks":[{"type":"command","command":"existing-hook","timeout":3}]},
              {"matcher":"{{{OldMatcher}}}","hooks":[{"type":"command","command":"codemuster hook","timeout":5}]}
            ]}}
            """;

        await InstallAsync("claude");

        var entries = Entries("claude");
        Assert.Equal(2, entries.Count);
        Assert.Equal("Edit", entries[0]["matcher"]!.GetValue<string>());
        Assert.Equal(3, entries[0]["hooks"]![0]!["timeout"]!.GetValue<int>());
        Assert.Contains("PowerShell", entries[1]["matcher"]!.GetValue<string>());
        Assert.Equal(10, entries[1]["hooks"]![0]!["timeout"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_codemuster_handler_sharing_an_entry_moves_out_so_the_other_handler_keeps_its_matcher()
    {
        fileSystem.Files[Path.Combine(Root, ".claude", "settings.json")] = $$$"""
            {"hooks":{"PostToolUse":[
              {"matcher":"{{{OldMatcher}}}","hooks":[
                {"type":"command","command":"existing-hook"},
                {"type":"command","command":"codemuster hook","timeout":10}]}
            ]}}
            """;

        await InstallAsync("claude");

        var entries = Entries("claude");
        Assert.Equal(2, entries.Count);
        Assert.Equal(OldMatcher, entries[0]["matcher"]!.GetValue<string>());
        Assert.Equal("existing-hook", Assert.Single(entries[0]["hooks"]!.AsArray())!["command"]!.GetValue<string>());
        Assert.Contains("PowerShell", entries[1]["matcher"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("gemini")]
    public async Task A_rerun_with_current_hooks_leaves_the_settings_file_alone(string agent)
    {
        await InstallAsync(agent);
        var settings = fileSystem.Files[SettingsPath(agent)];
        fileSystem.Files[SettingsPath(agent)] = settings.Replace("\n", "\r\n", StringComparison.Ordinal);
        var writes = fileSystem.Writes;

        await InstallAsync(agent);

        Assert.Equal(settings.Replace("\n", "\r\n", StringComparison.Ordinal), fileSystem.Files[SettingsPath(agent)]);
        Assert.Equal(writes, fileSystem.Writes);
    }

    [Fact]
    public async Task Each_agent_reports_whether_its_skill_and_hook_were_added_repaired_or_already_current()
    {
        var path = SettingsPath("claude");
        fileSystem.Files[path] = """
            // user comment
            {"permissions":{"allow":["Bash(ls)"]},}
            """;

        var added = await new AgentSetup(fileSystem).InstallAsync(Root, ["claude"], hooks: true, "skill", CancellationToken.None);
        var current = await new AgentSetup(fileSystem).InstallAsync(Root, ["claude"], hooks: true, "skill", CancellationToken.None);
        fileSystem.Files[path] = fileSystem.Files[path].Replace("|PowerShell", "", StringComparison.Ordinal);
        var repaired = await new AgentSetup(fileSystem).InstallAsync(Root, ["claude"], hooks: true, "skill", CancellationToken.None);
        var skillOnly = await new AgentSetup(fileSystem).InstallAsync(Root, ["claude"], hooks: false, "new skill", CancellationToken.None);

        Assert.Equal([new AgentSetupResult("claude", true, HookChange.Added, path, true)], added);
        Assert.Equal([new AgentSetupResult("claude", false, HookChange.Current, path, false)], current);
        Assert.Equal([new AgentSetupResult("claude", false, HookChange.Repaired, path, true)], repaired);
        Assert.Equal([new AgentSetupResult("claude", true, HookChange.None, null, false)], skillOnly);
    }

    private JsonObject CodeMusterEntry(string agent) =>
        Entries(agent).Single(entry => entry["hooks"]!.AsArray().Any(handler => handler!["command"]!.GetValue<string>() == "codemuster hook"));

    private List<JsonObject> Entries(string agent) =>
        [.. JsonNode.Parse(fileSystem.Files[SettingsPath(agent)])!["hooks"]![agent == "gemini" ? "AfterTool" : "PostToolUse"]!.AsArray().Select(entry => entry!.AsObject())];

    private static SortedSet<string> PluginTools()
    {
        var hooks = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "distribution", "hooks", "hooks.json")))!;
        return Tools(hooks["hooks"]!["PostToolUse"]![0]!["matcher"]!.GetValue<string>());
    }

    private static SortedSet<string> Tools(string matcher) =>
        new(matcher.TrimStart('^').TrimEnd('$').Trim('(', ')').Split('|'), StringComparer.Ordinal);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMuster.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CodeMuster.sln not found above " + AppContext.BaseDirectory);
    }
}
