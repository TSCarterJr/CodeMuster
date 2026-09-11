using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class CodexAdapter(string executable) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments => ArgumentsFor(NewLastMessageFile());

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

    private static IReadOnlyList<string> ArgumentsFor(string lastMessageFile) =>
        ["exec", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral", "--color", "never", "--output-last-message", lastMessageFile, "-"];
}
