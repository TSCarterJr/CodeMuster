using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GeminiAdapter : IAgentAdapter
{
    private readonly string _executable;
    private readonly string? _workingDirectory;
    private readonly bool _write;

    public GeminiAdapter(string executable, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null)
    {
        if (effort is not null)
        {
            throw new ArgumentException("gemini has no effort level; drop --effort or run this kind with another agent", nameof(effort));
        }

        _executable = executable;
        _workingDirectory = workingDirectory;
        _write = write;
        Arguments = ["--output-format", "json", "--approval-mode", write ? "auto_edit" : "default", "--allowed-mcp-server-names", "__codemuster_no_mcp__", "--extensions", "none", .. model is null ? Array.Empty<string>() : ["-m", model]];
        Identity = new AgentIdentity("gemini", model, null);
    }

    public IReadOnlyList<string> Arguments { get; }

    public AgentIdentity Identity { get; }

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken)
    {
        var system = Environment.GetEnvironmentVariable("GEMINI_CLI_SYSTEM_SETTINGS_PATH")
            ?? (OperatingSystem.IsWindows() ? "C:/ProgramData/gemini-cli/settings.json"
                : OperatingSystem.IsMacOS() ? "/Library/Application Support/GeminiCli/settings.json" : "/etc/gemini-cli/settings.json");
        var file = Path.Combine(Path.GetTempPath(), $"codemuster-gemini-{Guid.NewGuid():N}.json");
        try
        {
            var original = File.Exists(system) ? await File.ReadAllTextAsync(system, cancellationToken) : "{}";
            await File.WriteAllTextAsync(file, RestrictedSettings(original, _write), cancellationToken);
            return FinalText(await HeadlessProcess.RunAsync(_executable, Arguments, pack, cancellationToken, _workingDirectory,
                new Dictionary<string, string>
                {
                    ["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = file,
                    ["GEMINI_CLI_SYSTEM_DEFAULTS_PATH"] = Environment.GetEnvironmentVariable("GEMINI_CLI_SYSTEM_DEFAULTS_PATH") ?? Path.Combine(Path.GetDirectoryName(system)!, "system-defaults.json"),
                }));
        }
        finally { File.Delete(file); }
    }

    internal static string RestrictedSettings(string original, bool write)
    {
        var settings = JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        var tools = settings["tools"] as JsonObject ?? new JsonObject();
        string[] allowed = ["list_directory", "read_file", "read_many_files", "glob", "search_file_content", .. write ? new[] { "write_file", "replace" } : []];
        if (tools["core"] is JsonArray prior) allowed = allowed.Where(name => prior.Any(value => value?.GetValue<string>() == name)).ToArray();
        tools["core"] = new JsonArray(allowed.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        tools["discoveryCommand"] = "";
        tools["callCommand"] = "";
        if (settings["tools"] is null) settings["tools"] = tools;
        return settings.ToJsonString();
    }

    internal static string FinalText(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"gemini printed no JSON envelope: {output.Trim()}");
        }

        try
        {
            using var document = JsonDocument.Parse(output[start..(end + 1)]);
            return document.RootElement.TryGetProperty("response", out var response)
                ? response.GetString() ?? ""
                : throw new InvalidOperationException($"gemini printed no response: {output.Trim()}");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"gemini printed invalid JSON: {output.Trim()}");
        }
    }
}
