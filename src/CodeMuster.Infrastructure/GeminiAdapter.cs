using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GeminiAdapter : IAgentAdapter
{
    private readonly string _executable;

    public GeminiAdapter(string executable, string? model = null, string? effort = null, bool write = false)
    {
        if (effort is not null)
        {
            throw new ArgumentException("gemini has no effort level; drop --effort or run this kind with another agent", nameof(effort));
        }

        _executable = executable;
        Arguments = ["--output-format", "json", "--approval-mode", write ? "auto_edit" : "default", .. model is null ? Array.Empty<string>() : ["-m", model]];
        Identity = new AgentIdentity("gemini", model, null);
    }

    public IReadOnlyList<string> Arguments { get; }

    public AgentIdentity Identity { get; }

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken) =>
        FinalText(await HeadlessProcess.RunAsync(_executable, Arguments, pack, cancellationToken).ConfigureAwait(false));

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
