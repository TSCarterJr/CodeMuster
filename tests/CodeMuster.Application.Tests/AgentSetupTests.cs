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
    public async Task A_rerun_keeps_a_codemuster_timeout_the_user_raised()
    {
        fileSystem.Files[Path.Combine(Root, ".claude", "settings.json")] = $$$"""
            {"hooks":{"PostToolUse":[
              {"matcher":"{{{OldMatcher}}}","hooks":[{"type":"command","command":"codemuster hook","timeout":30}]}
            ]}}
            """;

        await InstallAsync("claude");

        var entries = Entries("claude");
        Assert.Contains("PowerShell", entries[0]["matcher"]!.GetValue<string>());
        Assert.Equal(30, entries[0]["hooks"]![0]!["timeout"]!.GetValue<int>());
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

    private Task<IReadOnlyList<McpSetupResult>> InstallMcpAsync(params string[] agents) =>
        new AgentSetup(fileSystem).InstallMcpAsync(Root, agents, CancellationToken.None);

    private static JsonNode PluginServer() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "distribution", "mcp.json")))!["mcpServers"]!["codemuster"]!;

    [Theory]
    [InlineData("claude", ".mcp.json")]
    [InlineData("gemini", ".gemini/settings.json")]
    public async Task The_mcp_server_is_added_to_json_settings_as_the_plugin_registers_it(string agent, string relative)
    {
        var path = Path.Combine([Root, .. relative.Split('/')]);

        var results = await InstallMcpAsync(agent);

        Assert.Equal([new McpSetupResult(agent, path, true, false)], results);
        var server = JsonNode.Parse(fileSystem.Files[path])!["mcpServers"]!["codemuster"]!;
        Assert.True(JsonNode.DeepEquals(PluginServer(), server), server.ToJsonString());
    }

    [Fact]
    public async Task The_mcp_server_is_appended_to_codex_config_toml_with_the_plugin_command()
    {
        var path = Path.Combine(Root, ".codex", "config.toml");

        var results = await InstallMcpAsync("codex");

        Assert.Equal([new McpSetupResult("codex", path, true, false)], results);
        var args = PluginServer()["args"]!.AsArray().Select(arg => arg!.GetValue<string>()).ToList();
        Assert.Equal(
            $"[mcp_servers.codemuster]\ncommand = \"{PluginServer()["command"]!.GetValue<string>()}\"\nargs = [\"{args[0]}\", '{args[1]}']\n",
            fileSystem.Files[path]);
    }

    [Fact]
    public async Task Adding_the_mcp_server_keeps_other_servers_and_settings_and_a_rerun_changes_nothing()
    {
        var claude = Path.Combine(Root, ".mcp.json");
        var gemini = Path.Combine(Root, ".gemini", "settings.json");
        var codex = Path.Combine(Root, ".codex", "config.toml");
        fileSystem.Files[claude] = """{"mcpServers":{"docs":{"command":"docs-mcp","args":["--stdio"]}}}""";
        fileSystem.Files[gemini] = """{"theme":"dark","mcpServers":{"docs":{"command":"docs-mcp"}}}""";
        fileSystem.Files[codex] = "model = \"o3\"\n\n[mcp_servers.docs]\ncommand = \"docs-mcp\"\n";

        var first = await InstallMcpAsync("claude", "codex", "gemini");
        var written = new Dictionary<string, string>(fileSystem.Files);
        var second = await InstallMcpAsync("claude", "codex", "gemini");

        Assert.Equal([true, true, true], first.Select(result => result.Added));
        Assert.Equal([true, false, true], first.Select(result => result.RewroteSettings));
        Assert.Equal("docs-mcp", JsonNode.Parse(fileSystem.Files[claude])!["mcpServers"]!["docs"]!["command"]!.GetValue<string>());
        Assert.Equal("dark", JsonNode.Parse(fileSystem.Files[gemini])!["theme"]!.GetValue<string>());
        Assert.NotNull(JsonNode.Parse(fileSystem.Files[gemini])!["mcpServers"]!["docs"]);
        Assert.StartsWith("model = \"o3\"\n\n[mcp_servers.docs]\ncommand = \"docs-mcp\"\n\n[mcp_servers.codemuster]\n", fileSystem.Files[codex]);
        Assert.Equal([false, false, false], second.Select(result => result.Added));
        Assert.Equal(written, fileSystem.Files);
    }

    [Fact]
    public async Task A_codemuster_server_the_user_configured_is_left_alone()
    {
        var claude = Path.Combine(Root, ".mcp.json");
        var codex = Path.Combine(Root, ".codex", "config.toml");
        fileSystem.Files[claude] = """{"mcpServers":{"codemuster":{"command":"codemuster","args":["mcp","--refresh"]}}}""";
        fileSystem.Files[codex] = "[mcp_servers.codemuster]\ncommand = \"codemuster\"\n";
        var before = new Dictionary<string, string>(fileSystem.Files);

        var results = await InstallMcpAsync("claude", "codex");

        Assert.Equal([false, false], results.Select(result => result.Added));
        Assert.Equal(before, fileSystem.Files);
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
