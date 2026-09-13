using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

/// <summary>The working tree, as fix mode reads and commits it (D37). Commits locally; never pushes.</summary>
public sealed class GitWorkspace(string repoRoot) : IWorkspace
{
    public async Task<bool> IsCleanAsync(CancellationToken cancellationToken)
    {
        var status = await GitProcess.RunAsync(repoRoot, ["status", "--porcelain", "--untracked-files=no"], null, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(status);
    }

    public async Task CommitAsync(string message, CancellationToken cancellationToken)
    {
        await GitProcess.RunAsync(repoRoot, ["add", "--update"], null, cancellationToken).ConfigureAwait(false);
        await GitProcess.RunAsync(repoRoot, ["commit", "-m", message], null, cancellationToken).ConfigureAwait(false);
    }

    public Task RestoreAsync(CancellationToken cancellationToken) =>
        GitProcess.RunAsync(repoRoot, ["checkout", "--", "."], null, cancellationToken);
}
