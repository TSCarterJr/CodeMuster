using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class OpenCodeAdapter(string executable) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } = ["run", "--agent", "plan", "--format", "json"];

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

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "error":
                    throw new InvalidOperationException($"opencode reported an error: {root.GetProperty("error").GetRawText()}");
                case "text":
                    text = root.GetProperty("part").GetProperty("text").GetString();
                    break;
            }
        }

        return text ?? throw new InvalidOperationException($"opencode printed no text: {output.Trim()}");
    }
}
