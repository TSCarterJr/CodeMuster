using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class CodexAdapter(string executable, string? model = null, string? effort = null) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments => ArgumentsFor(NewLastMessageFile());

    public AgentIdentity Identity { get; } = new("codex", model, effort);

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken)
    {
        var lastMessageFile = NewLastMessageFile();
        try
        {
            await HeadlessProcess.RunAsync(executable, ArgumentsFor(lastMessageFile), pack, cancellationToken).ConfigureAwait(false);
            return File.Exists(lastMessageFile)
                ? await File.ReadAllTextAsync(lastMessageFile, cancellationToken).ConfigureAwait(false)
                : throw new InvalidOperationException("codex exited without writing its last message.");
        }
        finally
        {
            File.Delete(lastMessageFile);
        }
    }

    private static string NewLastMessageFile() =>
        Path.Combine(Path.GetTempPath(), $"codemuster-codex-{Guid.NewGuid():N}.md");

    private IReadOnlyList<string> ArgumentsFor(string lastMessageFile) =>
    [
        "exec", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral", "--color", "never",
        .. model is null ? Array.Empty<string>() : ["-m", model],
        .. effort is null ? Array.Empty<string>() : ["-c", "model_reasoning_effort=" + Quoted(effort)],
        "--output-last-message", lastMessageFile, "-",
    ];

    private static string Quoted(string value) => "\"" + value + "\"";
}
