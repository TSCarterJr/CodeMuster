using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GeminiAdapter(string executable) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } = ["--output-format", "json", "--approval-mode", "default"];

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken) =>
        FinalText(await HeadlessProcess.RunAsync(executable, Arguments, pack, cancellationToken).ConfigureAwait(false));

    internal static string FinalText(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"gemini printed no JSON envelope: {output.Trim()}");
        }

        using var document = JsonDocument.Parse(output[start..(end + 1)]);
        return document.RootElement.TryGetProperty("response", out var response)
            ? response.GetString() ?? ""
            : throw new InvalidOperationException($"gemini printed no response: {output.Trim()}");
    }
}
