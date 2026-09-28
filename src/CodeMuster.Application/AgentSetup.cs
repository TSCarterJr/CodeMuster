using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Installs selected project skills and merges CodeMuster's change hook into existing agent settings.</summary>
public sealed class AgentSetup(IFileSystem files)
{
    /// <summary>Agents supported by integrated setup.</summary>
    public static IReadOnlyList<string> Agents { get; } = ["claude", "codex", "gemini"];

    /// <summary>The program agents start for the codemuster MCP server (D73); distribution/mcp.json registers the same command for the plugin.</summary>
    public const string McpCommand = "node";

    /// <summary>
    /// Its arguments. npm installs codemuster as a .cmd shim on Windows, which agents cannot start without a shell,
    /// so node starts <c>codemuster mcp</c> through the shell on every platform and passes standard input and output through.
    /// </summary>
    public static IReadOnlyList<string> McpArguments { get; } =
        ["-e", "process.exitCode=require(\"node:child_process\").spawnSync(\"codemuster mcp\",{stdio:\"inherit\",shell:true}).status??1"];

    private static readonly Regex CodexServer = new(@"^\s*\[\s*mcp_servers\s*\.\s*(codemuster|""codemuster"")\s*\]", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // The Claude and Codex tools that run the change hook; distribution/hooks/hooks.json matches the same tools.
    private const string ToolMatcher = "^(Edit|Write|apply_patch|Bash|PowerShell|NotebookEdit)$";

    /// <summary>Validates a comma-separated selection before setup writes any files.</summary>
    public static IReadOnlyList<string> Select(string selection)
    {
        var agents = selection == "all" ? Agents : selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (agents.Count == 0 || agents.Any(a => !Agents.Contains(a)))
        {
            throw new ArgumentException("choose claude, codex, gemini, or all with --for");
        }
        return agents.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Installs skills and optional hooks, preserving unrelated settings and existing hooks, and reports what it did for each agent.</summary>
    public async Task<IReadOnlyList<AgentSetupResult>> InstallAsync(string root, IReadOnlyList<string> agents, bool hooks, string skill, CancellationToken cancellationToken)
    {
        var settings = new List<(string Path, string Text)>();
        var changes = new Dictionary<string, (HookChange Hook, string? Path, bool Rewrote)>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (!Agents.Contains(agent)) throw new ArgumentException("unsupported agent: " + agent);
            if (!hooks)
            {
                changes[agent] = (HookChange.None, null, false);
                continue;
            }

            var path = Path.Combine(root, "." + agent, agent == "codex" ? "hooks.json" : "settings.json");
            var existed = files.FileExists(path);
            var text = existed ? await files.ReadAllTextAsync(path, cancellationToken) : "{}";
            var document = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw new ArgumentException($"{path} must contain a JSON object; existing settings were not changed");
            var hookMap = document["hooks"] as JsonObject;
            if (document["hooks"] is not null && hookMap is null) throw new ArgumentException($"invalid hooks in {path}");
            if (hookMap is null) document["hooks"] = hookMap = new JsonObject();
            var eventName = agent == "gemini" ? "AfterTool" : "PostToolUse";
            var entries = hookMap[eventName] as JsonArray;
            if (hookMap[eventName] is not null && entries is null) throw new ArgumentException($"invalid {eventName} hooks in {path}");
            if (entries is null) hookMap[eventName] = entries = new JsonArray();
            var before = document.ToJsonString();
            var matcher = agent == "gemini" ? "write_file|replace|run_shell_command" : ToolMatcher;
            var timeout = agent == "gemini" ? 10000 : 10;
            foreach (var entry in entries.OfType<JsonObject>().Where(e => Handlers(e).Any(IsCodeMuster)).ToList())
            {
                if (Handlers(entry).All(IsCodeMuster))
                {
                    entry["matcher"] = matcher;
                    foreach (var handler in Handlers(entry)) handler["timeout"] = Math.Max(timeout, handler["timeout"] is JsonValue value && value.TryGetValue<int>(out var existing) ? existing : 0);
                }
                else
                {
                    // Another hook shares this entry, so its matcher stays and CodeMuster's handler moves to an entry of its own.
                    var shared = (JsonArray)entry["hooks"]!;
                    foreach (var handler in Handlers(entry).Where(IsCodeMuster)) shared.Remove(handler);
                }
            }

            var added = !entries.OfType<JsonObject>().Any(e => Handlers(e).Any(IsCodeMuster));
            if (added)
            {
                var handler = new JsonObject { ["type"] = "command", ["command"] = "codemuster hook", ["timeout"] = timeout };
                if (agent == "gemini") handler["name"] = "codemuster-changes";
                entries.Add(new JsonObject { ["matcher"] = matcher, ["hooks"] = new JsonArray(handler) });
            }

            var changed = document.ToJsonString() != before;
            if (changed) settings.Add((path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
            changes[agent] = (added ? HookChange.Added : changed ? HookChange.Repaired : HookChange.Current, path, changed && existed);
        }
        foreach (var setting in settings) await files.WriteAllTextAsync(setting.Path, setting.Text, cancellationToken);
        var results = new List<AgentSetupResult>();
        foreach (var agent in agents.Distinct(StringComparer.Ordinal))
        {
            var skillPath = SkillInstaller.PathFor(agent, false, root, "");
            var skillWritten = !files.FileExists(skillPath) || await files.ReadAllTextAsync(skillPath, cancellationToken) != skill;
            await new SkillInstaller(files).InstallAsync(agent, false, root, "", skill, cancellationToken);
            var (hook, settingsPath, rewrote) = changes[agent];
            results.Add(new AgentSetupResult(agent, skillWritten, hook, settingsPath, rewrote));
        }

        return results;
    }

    /// <summary>
    /// Registers the codemuster MCP server in each agent's project settings (D73): <c>.mcp.json</c> for Claude, <c>.codex/config.toml</c> for Codex and
    /// <c>.gemini/settings.json</c> for Gemini. A server already named codemuster is left as it is, and every other server and setting is kept.
    /// </summary>
    public async Task<IReadOnlyList<McpSetupResult>> InstallMcpAsync(string root, IReadOnlyList<string> agents, CancellationToken cancellationToken)
    {
        var results = new List<McpSetupResult>();
        foreach (var agent in agents.Distinct(StringComparer.Ordinal))
        {
            if (!Agents.Contains(agent)) throw new ArgumentException("unsupported agent: " + agent);
            var path = agent switch
            {
                "claude" => Path.Combine(root, ".mcp.json"),
                "codex" => Path.Combine(root, ".codex", "config.toml"),
                _ => Path.Combine(root, ".gemini", "settings.json"),
            };
            var existed = files.FileExists(path);
            var text = existed ? await files.ReadAllTextAsync(path, cancellationToken) : "";
            string? updated;
            if (agent == "codex")
            {
                // No TOML parser is allowed, so the server table is appended when no table of that name exists; the rest of the file is untouched.
                var table = $"[mcp_servers.codemuster]\ncommand = \"{McpCommand}\"\nargs = [\"{McpArguments[0]}\", '{McpArguments[1]}']\n";
                updated = CodexServer.IsMatch(text) ? null : text.Length == 0 ? table : text + (text.EndsWith('\n') ? "\n" : "\n\n") + table;
            }
            else
            {
                var document = JsonNode.Parse(existed ? text : "{}", documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                    ?? throw new ArgumentException($"{path} must contain a JSON object; existing settings were not changed");
                var servers = document["mcpServers"] as JsonObject;
                if (document["mcpServers"] is not null && servers is null) throw new ArgumentException($"invalid mcpServers in {path}");
                if (servers is null) document["mcpServers"] = servers = new JsonObject();
                updated = null;
                if (!servers.ContainsKey("codemuster"))
                {
                    servers["codemuster"] = new JsonObject { ["command"] = McpCommand, ["args"] = new JsonArray([.. McpArguments.Select(arg => (JsonNode?)arg)]) };
                    updated = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
                }
            }

            if (updated is not null) await files.WriteAllTextAsync(path, updated, cancellationToken);
            results.Add(new McpSetupResult(agent, path, updated is not null, updated is not null && existed && agent != "codex"));
        }

        return results;
    }

    private static List<JsonObject> Handlers(JsonObject entry) => entry["hooks"] is JsonArray handlers ? [.. handlers.OfType<JsonObject>()] : [];

    private static bool IsCodeMuster(JsonObject handler) =>
        handler["command"] is JsonValue command && command.TryGetValue<string>(out var text) && text == "codemuster hook";
}
