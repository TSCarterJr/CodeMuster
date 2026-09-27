using System.Globalization;
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
            throw new ArgumentException("gemini has no effort level; drop --effort or run this kind with another agent");
        }

        _executable = executable;
        _workingDirectory = workingDirectory;
        _write = write;
        Arguments = ["--output-format", "json", "--approval-mode", write ? "auto_edit" : "default", "--allowed-mcp-server-names", "__codemuster_no_mcp__", "--extensions", "none", .. model is null ? Array.Empty<string>() : ["-m", model]];
        Identity = new AgentIdentity("gemini", model, null);
    }

    public IReadOnlyList<string> Arguments { get; }

    public AgentIdentity Identity { get; }

    public async Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken)
    {
        var system = Environment.GetEnvironmentVariable("GEMINI_CLI_SYSTEM_SETTINGS_PATH")
            ?? (OperatingSystem.IsWindows() ? "C:/ProgramData/gemini-cli/settings.json"
                : OperatingSystem.IsMacOS() ? "/Library/Application Support/GeminiCli/settings.json" : "/etc/gemini-cli/settings.json");
        var file = Path.Combine(Path.GetTempPath(), $"codemuster-gemini-{Guid.NewGuid():N}.json");
        try
        {
            var original = File.Exists(system) ? await File.ReadAllTextAsync(system, cancellationToken) : "{}";
            await File.WriteAllTextAsync(file, RestrictedSettings(original, _write), cancellationToken);
            return Reply(await HeadlessProcess.CaptureAsync(_executable, Arguments, pack, cancellationToken, _workingDirectory,
                new Dictionary<string, string>
                {
                    ["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = file,
                    ["GEMINI_CLI_SYSTEM_DEFAULTS_PATH"] = Environment.GetEnvironmentVariable("GEMINI_CLI_SYSTEM_DEFAULTS_PATH") ?? Path.Combine(Path.GetDirectoryName(system)!, "system-defaults.json"),
                }).ConfigureAwait(false), _executable, Arguments);
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

    // On failure gemini prints nothing on stdout and its JSON error object on stderr, after any warnings, so a failed call's usage is unknown.
    internal static AgentReply Reply(ProcessOutput output, string executable, IReadOnlyList<string> arguments)
    {
        var usage = Usage(output.Output);
        if (output.ExitCode != 0)
        {
            var start = output.Error.IndexOf('{');
            using var error = start < 0 ? null : UsageJson.TryParse(output.Error[start..(output.Error.LastIndexOf('}') + 1)]);
            var message = error?.RootElement is { } root && UsageJson.Object(root, "error") is { } detail ? UsageJson.String(detail, "message") : null;
            throw new AgentCallException(message is null ? output.Failure(executable, arguments)
                : string.Create(CultureInfo.InvariantCulture, $"gemini reported an error (exit code {output.ExitCode}): {message.Trim()}"), usage);
        }

        try
        {
            return new AgentReply(FinalText(output.Output), usage);
        }
        catch (InvalidOperationException ex)
        {
            throw new AgentCallException(ex.Message, usage, ex);
        }
    }

    // stats.models has one entry per model the call used; prompt includes the cached tokens, and thoughts are billed as output on top of candidates.
    internal static AgentUsage Usage(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        using var document = start < 0 || end < start ? null : UsageJson.TryParse(output[start..(end + 1)]);
        if (document?.RootElement is not { } root || UsageJson.Object(root, "stats") is not { } stats || UsageJson.Object(stats, "models") is not { } models)
            return AgentUsage.Unknown;
        var entries = models.EnumerateObject().Select(entry => (entry.Name, Tokens: UsageJson.Object(entry.Value, "tokens"))).Where(entry => entry.Tokens is not null).ToList();
        if (entries.Count == 0) return AgentUsage.Unknown;
        long? Total(string name) => entries.Select(entry => UsageJson.Long(entry.Tokens, name)).Aggregate((long?)null, UsageJson.Sum);
        var prompt = Total("prompt");
        var cached = Total("cached");
        var input = prompt is { } all ? Math.Max(0, all - (cached ?? 0)) + (Total("tool") ?? 0) : (long?)null;
        var answered = entries.OrderByDescending(entry => UsageJson.Long(entry.Tokens, "total") ?? 0).First().Name;
        return new AgentUsage(input, UsageJson.Sum(Total("candidates"), Total("thoughts")), cached, null, answered, null);
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
