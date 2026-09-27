using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class CodexAdapter(string executable, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) : IAgentAdapter
{
    private const string Rerouted = "model rerouted: ";

    public IReadOnlyList<string> Arguments => ArgumentsFor(NewLastMessageFile());

    public AgentIdentity Identity { get; } = new("codex", model, effort);

    public async Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken)
    {
        var lastMessageFile = NewLastMessageFile();
        try
        {
            var arguments = ArgumentsFor(lastMessageFile);
            var output = await HeadlessProcess.CaptureAsync(executable, arguments, pack, cancellationToken, workingDirectory).ConfigureAwait(false);
            var lastMessage = output.ExitCode == 0 && File.Exists(lastMessageFile)
                ? await File.ReadAllTextAsync(lastMessageFile, cancellationToken).ConfigureAwait(false)
                : null;
            return Reply(output, lastMessage, executable, arguments);
        }
        finally
        {
            File.Delete(lastMessageFile);
        }
    }

    // The answer is the last-message file; the --json event stream on stdout carries only the usage.
    internal static AgentReply Reply(ProcessOutput output, string? lastMessage, string executable, IReadOnlyList<string> arguments)
    {
        var usage = Usage(output.Output);
        if (output.ExitCode != 0) throw new AgentCallException(output.Failure(executable, arguments), usage);
        return lastMessage is null
            ? throw new AgentCallException("codex exited without writing its last message.", usage)
            : new AgentReply(lastMessage, usage);
    }

    // turn.completed carries the thread's running total, so the last one is the call's usage; input_tokens includes the cached tokens.
    internal static AgentUsage Usage(string output)
    {
        var usage = AgentUsage.Unknown;
        string? answered = null;
        foreach (var document in UsageJson.Lines(output))
        {
            using (document)
            {
                var root = document.RootElement;
                var type = UsageJson.String(root, "type");
                if (type == "turn.completed" && UsageJson.Object(root, "usage") is { } totals)
                {
                    var input = UsageJson.Long(totals, "input_tokens");
                    var cached = UsageJson.Long(totals, "cached_input_tokens");
                    usage = new AgentUsage(input is { } all ? Math.Max(0, all - (cached ?? 0)) : null, UsageJson.Long(totals, "output_tokens"),
                        cached, UsageJson.Long(totals, "cache_write_input_tokens"), null, null);
                }
                else if (type == "item.completed" && UsageJson.Object(root, "item") is { } item && UsageJson.String(item, "type") == "error"
                    && UsageJson.String(item, "message") is { } message && message.StartsWith(Rerouted, StringComparison.Ordinal))
                {
                    var target = message[Rerouted.Length..];
                    var arrow = target.IndexOf("-> ", StringComparison.Ordinal);
                    if (arrow >= 0) answered = target[(arrow + 3)..].Split(' ', 2)[0];
                }
            }
        }

        return usage.HasTokens && answered is not null ? usage with { Model = answered } : usage;
    }

    private static string NewLastMessageFile() =>
        Path.Combine(Path.GetTempPath(), $"codemuster-codex-{Guid.NewGuid():N}.md");

    private IReadOnlyList<string> ArgumentsFor(string lastMessageFile) =>
    [
        "exec", "--sandbox", write ? "workspace-write" : "read-only", "--skip-git-repo-check", "--ephemeral", "--color", "never",
        "--disable", "shell_tool",
        .. model is null ? Array.Empty<string>() : ["-m", model],
        .. effort is null ? Array.Empty<string>() : ["-c", "model_reasoning_effort=" + Quoted(effort)],
        "--json", "--output-last-message", lastMessageFile, "-",
    ];

    private static string Quoted(string value) => "\"" + value + "\"";
}
