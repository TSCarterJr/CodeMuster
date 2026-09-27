using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class ClaudeAdapter(string executable, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } =
    [
        "--print", "--output-format", "json",
        "--permission-mode", write ? "acceptEdits" : "dontAsk",
        "--strict-mcp-config", "--no-session-persistence",
        "--tools", write ? "Read,Glob,Grep,Edit,Write" : "Read,Glob,Grep",
        .. model is null ? Array.Empty<string>() : ["--model", model],
        .. effort is null ? Array.Empty<string>() : ["--effort", effort],
    ];

    public AgentIdentity Identity { get; } = new("claude", model, effort);

    public async Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken) =>
        Reply(await HeadlessProcess.CaptureAsync(executable, Arguments, pack, cancellationToken, workingDirectory).ConfigureAwait(false), executable, Arguments);

    // The JSON result envelope carries the response in "result"; output that is not an envelope is taken as the response itself, with usage unknown.
    internal static AgentReply Reply(ProcessOutput output, string executable, IReadOnlyList<string> arguments)
    {
        using var document = UsageJson.TryParse(output.Output.Trim());
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("result", out _) && !root.TryGetProperty("is_error", out _))
        {
            return output.ExitCode == 0
                ? new AgentReply(output.Output, AgentUsage.Unknown)
                : throw new AgentCallException(output.Failure(executable, arguments), AgentUsage.Unknown);
        }

        var usage = Usage(root);
        var result = UsageJson.String(root, "result");
        var isError = root.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (!isError && output.ExitCode == 0 && result is not null) return new AgentReply(result, usage);

        var errors = root.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())
            : [];
        var detail = string.Join("; ", new[] { result }.Concat(errors).Append(output.Error.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
        var exit = output.ExitCode == 0 ? "" : $", exit code {output.ExitCode}";
        throw new AgentCallException($"claude reported an error ({UsageJson.String(root, "subtype") ?? "no subtype"}{exit}): {detail}", usage);
    }

    // modelUsage covers every model the call used, helpers and subagents included; usage covers only the main loop but is the only place that splits cache writes by lifetime.
    private static AgentUsage Usage(JsonElement root)
    {
        var main = UsageJson.Object(root, "usage");
        var longWrites = UsageJson.Long(main is { } usage ? UsageJson.Object(usage, "cache_creation") : null, "ephemeral_1h_input_tokens");
        var cost = UsageJson.Decimal(root, "total_cost_usd");
        var models = UsageJson.Object(root, "modelUsage") is { } byModel
            ? byModel.EnumerateObject().Where(entry => entry.Value.ValueKind == JsonValueKind.Object).ToList()
            : [];
        if (models.Count == 0)
        {
            return new AgentUsage(UsageJson.Long(main, "input_tokens"), UsageJson.Long(main, "output_tokens"),
                UsageJson.Long(main, "cache_read_input_tokens"), UsageJson.Long(main, "cache_creation_input_tokens"), null, cost)
            { CacheWrite1hTokens = longWrites };
        }

        long? Total(string name) => models.Select(entry => UsageJson.Long(entry.Value, name)).Aggregate((long?)null, UsageJson.Sum);
        var answered = models
            .OrderByDescending(entry => UsageJson.Decimal(entry.Value, "costUSD") ?? 0)
            .ThenByDescending(entry => UsageJson.Long(entry.Value, "outputTokens") ?? 0)
            .First();
        return new AgentUsage(Total("inputTokens"), Total("outputTokens"), Total("cacheReadInputTokens"), Total("cacheCreationInputTokens"),
            UsageJson.String(answered.Value, "canonicalModel") ?? answered.Name, cost)
        { CacheWrite1hTokens = longWrites };
    }
}
