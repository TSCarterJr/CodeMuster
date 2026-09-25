using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Installs selected project skills and merges CodeMuster's change hook into existing agent settings.</summary>
public sealed class AgentSetup(IFileSystem files)
{
    /// <summary>Agents supported by integrated setup.</summary>
    public static IReadOnlyList<string> Agents { get; } = ["claude", "codex", "gemini"];

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

    /// <summary>Installs skills and optional hooks, preserving unrelated settings and existing hooks.</summary>
    public async Task InstallAsync(string root, IReadOnlyList<string> agents, bool hooks, string skill, CancellationToken cancellationToken)
    {
        var settings = new List<(string Path, string Text)>();
        foreach (var agent in agents)
        {
            if (!Agents.Contains(agent)) throw new ArgumentException("unsupported agent: " + agent);
            if (!hooks) continue;
            var path = Path.Combine(root, "." + agent, agent == "codex" ? "hooks.json" : "settings.json");
            var text = files.FileExists(path) ? await files.ReadAllTextAsync(path, cancellationToken) : "{}";
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
                    foreach (var handler in Handlers(entry)) handler["timeout"] = timeout;
                }
                else
                {
                    // Another hook shares this entry, so its matcher stays and CodeMuster's handler moves to an entry of its own.
                    var shared = (JsonArray)entry["hooks"]!;
                    foreach (var handler in Handlers(entry).Where(IsCodeMuster)) shared.Remove(handler);
                }
            }

            if (!entries.OfType<JsonObject>().Any(e => Handlers(e).Any(IsCodeMuster)))
            {
                var handler = new JsonObject { ["type"] = "command", ["command"] = "codemuster hook", ["timeout"] = timeout };
                if (agent == "gemini") handler["name"] = "codemuster-changes";
                entries.Add(new JsonObject { ["matcher"] = matcher, ["hooks"] = new JsonArray(handler) });
            }

            if (document.ToJsonString() != before) settings.Add((path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        }
        foreach (var setting in settings) await files.WriteAllTextAsync(setting.Path, setting.Text, cancellationToken);
        foreach (var agent in agents) await new SkillInstaller(files).InstallAsync(agent, false, root, "", skill, cancellationToken);
    }

    private static List<JsonObject> Handlers(JsonObject entry) => entry["hooks"] is JsonArray handlers ? [.. handlers.OfType<JsonObject>()] : [];

    private static bool IsCodeMuster(JsonObject handler) =>
        handler["command"] is JsonValue command && command.TryGetValue<string>(out var text) && text == "codemuster hook";
}
