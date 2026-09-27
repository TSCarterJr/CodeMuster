using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class OpenCodeAdapter(string executable, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } =
    [
        "run", "--agent", "codemuster", "--format", "json",
        .. model is null ? Array.Empty<string>() : ["-m", model],
        .. effort is null ? Array.Empty<string>() : ["--variant", effort],
    ];

    public AgentIdentity Identity { get; } = new("opencode", model, effort);

    internal string Settings => JsonSerializer.Serialize(new
    {
        agent = new Dictionary<string, object>
        {
            ["codemuster"] = new
            {
                mode = "primary",
                permission = new Dictionary<string, string>
                {
                    ["*"] = "deny",
                    ["read"] = "allow",
                    ["glob"] = "allow",
                    ["grep"] = "allow",
                    ["edit"] = write ? "allow" : "deny",
                    ["bash"] = "deny",
                    ["task"] = "deny",
                },
            },
        },
    });

    public async Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken) =>
        Reply(await HeadlessProcess.CaptureAsync(executable, Arguments, pack, cancellationToken, workingDirectory,
            new Dictionary<string, string> { ["OPENCODE_CONFIG_CONTENT"] = Settings }).ConfigureAwait(false), executable, Arguments);

    internal static AgentReply Reply(ProcessOutput output, string executable, IReadOnlyList<string> arguments)
    {
        var usage = Usage(output.Output);
        if (output.ExitCode != 0) throw new AgentCallException(output.Failure(executable, arguments), usage);
        try
        {
            return new AgentReply(FinalText(output.Output), usage);
        }
        catch (InvalidOperationException ex)
        {
            throw new AgentCallException(ex.Message, usage, ex);
        }
    }

    // Each step_finish is one model call. Input excludes cache reads and writes, reasoning is billed as output, and a zero cost means models.dev has no price for the model, not that the call was free.
    internal static AgentUsage Usage(string output)
    {
        long? input = null, generated = null, cacheRead = null, cacheWrite = null;
        decimal? cost = null;
        foreach (var document in UsageJson.Lines(output))
        {
            using (document)
            {
                var root = document.RootElement;
                if (UsageJson.String(root, "type") != "step_finish" || UsageJson.Object(root, "part") is not { } part || UsageJson.Object(part, "tokens") is not { } tokens)
                    continue;
                var cache = UsageJson.Object(tokens, "cache");
                input = UsageJson.Sum(input, UsageJson.Long(tokens, "input"));
                generated = UsageJson.Sum(generated, UsageJson.Sum(UsageJson.Long(tokens, "output"), UsageJson.Long(tokens, "reasoning")));
                cacheRead = UsageJson.Sum(cacheRead, UsageJson.Long(cache, "read"));
                cacheWrite = UsageJson.Sum(cacheWrite, UsageJson.Long(cache, "write"));
                if (UsageJson.Decimal(part, "cost") is { } step) cost = (cost ?? 0) + step;
            }
        }

        var usage = new AgentUsage(input, generated, cacheRead, cacheWrite, null, null);
        return usage.HasTokens && cost > 0 ? usage with { ReportedCostUsd = cost } : usage;
    }

    internal static string FinalText(string output)
    {
        string? text = null;
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            using var document = Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "error")
            {
                throw new InvalidOperationException($"opencode reported an error: {(root.TryGetProperty("error", out var error) ? error.GetRawText() : line.Trim())}");
            }

            if (type == "text")
            {
                text = root.TryGetProperty("part", out var part) && part.TryGetProperty("text", out var value)
                    ? value.GetString()
                    : throw new InvalidOperationException($"opencode printed a text event without text: {line.Trim()}");
            }
        }

        return text ?? throw new InvalidOperationException($"opencode printed no text: {output.Trim()}");
    }

    private static JsonDocument Parse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"opencode printed an invalid JSON event: {line.Trim()}");
        }
    }
}
