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

    public async Task ApplyPatchAsync(string patch, CancellationToken cancellationToken)
    {
        if (patch.Length > 0)
        {
            await GitProcess.RunAsync(repoRoot, ["apply", "--whitespace=nowarn", "-"], patch, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> HasFileChangesAsync(string path, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAllowingFailureAsync(repoRoot, ["--literal-pathspecs", "diff", "--quiet", "HEAD", "--", path], null, cancellationToken).ConfigureAwait(false);
        return result.ExitCode switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(result.Error),
        };
    }

    public async Task CommitFileAsync(string path, string message, CancellationToken cancellationToken)
    {
        await GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "add", "--", path], null, cancellationToken).ConfigureAwait(false);
        await GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "commit", "--only", "-m", message, "--", path], null, cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitFilesAsync(IReadOnlyList<string> paths, string message, CancellationToken cancellationToken)
    {
        await GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "add", "--", .. paths], null, cancellationToken);
        await GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "commit", "--only", "-m", message, "--", .. paths], null, cancellationToken);
    }

    public Task RestoreFileAsync(string path, CancellationToken cancellationToken) =>
        GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", path], null, cancellationToken);

    public Task<string> StashAsync(CancellationToken cancellationToken) =>
        SaveStashAsync("codemuster fix: saved tracked changes", cancellationToken);

    public async Task RestoreStashAsync(string stash, CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsCleanAsync(cancellationToken).ConfigureAwait(false))
            {
                await SaveStashAsync($"codemuster fix: unfinished changes before restoring {stash}", cancellationToken).ConfigureAwait(false);
            }

            await GitProcess.RunAsync(repoRoot, ["stash", "apply", "--index", stash], null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"could not fully restore your tracked changes; your backup is retained in stash {stash}. Fix commits are kept. Inspect git status and resolve any conflicts. To reapply the backup from a clean working tree, use git stash apply --index {stash}. {ex.Message}", ex);
        }
    }

    private async Task<string> SaveStashAsync(string message, CancellationToken cancellationToken)
    {
        if (await IsCleanAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("no tracked changes to stash");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var before = await GitProcess.RunAllowingFailureAsync(repoRoot, ["rev-parse", "--verify", "refs/stash"], null, cancellationToken).ConfigureAwait(false);
        // Finish saving and identifying the backup even if cancellation arrives during git stash.
        await GitProcess.RunAsync(repoRoot, ["stash", "push", "-m", message], null, CancellationToken.None).ConfigureAwait(false);
        var after = await GitProcess.RunAllowingFailureAsync(repoRoot, ["rev-parse", "--verify", "refs/stash"], null, CancellationToken.None).ConfigureAwait(false);
        if (after.ExitCode != 0 || after.Output == before.Output)
        {
            throw new InvalidOperationException("git could not stash the tracked changes; check for dirty submodules before running fix");
        }

        return after.Output.Trim();
    }
}
