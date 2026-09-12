using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class OpenCodeAdapter(string executable, string? model = null, string? effort = null, bool write = false) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } =
    [
        "run", "--agent", write ? "build" : "plan", "--format", "json",
        .. model is null ? Array.Empty<string>() : ["-m", model],
        .. effort is null ? Array.Empty<string>() : ["--variant", effort],
    ];

    public AgentIdentity Identity { get; } = new("opencode", model, effort);

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken) =>
        FinalText(await HeadlessProcess.RunAsync(executable, Arguments, pack, cancellationToken).ConfigureAwait(false));

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
