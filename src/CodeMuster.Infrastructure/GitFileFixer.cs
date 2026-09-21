using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GitFileFixer(string repoRoot, Func<string, IAgentAdapter> adapterForDirectory, IProgress<string>? notes = null, IReadOnlyList<string>? relatedFiles = null) : IFileFixer, IDisposable
{
    private readonly SemaphoreSlim worktrees = new(1, 1);

    public async Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
    {
        var allowed = new[] { path }.Concat(relatedFiles ?? []).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var file in allowed)
        {
            if (Path.IsPathRooted(file) || file.Contains(':') || file.Split('/').Any(part => part is ".." or "." or "")) throw new ArgumentException("expected an exact repo-relative file path: " + file);
            await GitProcess.RunAsync(repoRoot, ["--literal-pathspecs", "ls-files", "--error-unmatch", "--", file], null, cancellationToken);
        }
        var directory = Path.Combine(Path.GetTempPath(), "codemuster-fix-" + Guid.NewGuid().ToString("N"));
        var created = false;
        var retain = false;
        try
        {
            await worktrees.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await GitProcess.RunAsync(repoRoot, ["worktree", "add", "--detach", directory, "HEAD"], null, CancellationToken.None).ConfigureAwait(false);
                created = true;
            }
            finally
            {
                worktrees.Release();
            }

            var baseline = (await GitProcess.RunAsync(directory, ["rev-parse", "HEAD"], null, cancellationToken).ConfigureAwait(false)).Trim();
            var scope = string.Join(", ", allowed);
            var instructions = pack + $"\n\nYou are working in an isolated worktree. The explicit allowed file scope is: {scope}. Read related allowed files as needed and make the smallest coherent repair. Do not commit, stage, or modify files outside this scope. The coordinator runs tests and commits the repair.\n";
            var response = await adapterForDirectory(directory).RunAsync(instructions, cancellationToken).ConfigureAwait(false);
            var changed = await GitProcess.RunAsync(directory, ["diff", "--name-only", "-z", baseline], null, cancellationToken).ConfigureAwait(false);
            var untracked = await GitProcess.RunAsync(directory, ["ls-files", "--others", "--exclude-standard", "-z"], null, cancellationToken).ConfigureAwait(false);
            var extraPaths = changed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(changedPath => !allowed.Contains(changedPath, StringComparer.Ordinal))
                .Concat(untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(untrackedPath => untrackedPath != ".impeccable/hook.cache.json"))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (extraPaths.Length > 0)
            {
                retain = true;
                throw new InvalidOperationException($"worker for {path} changed files outside its assigned file: {string.Join(", ", extraPaths)}; no changes were applied; worker retained at {directory}");
            }

            var patch = await GitProcess.RunAsync(directory, ["--literal-pathspecs", "diff", "--binary", baseline, "--", .. allowed], null, cancellationToken).ConfigureAwait(false);
            retain = true;
            return new FileFixEdit(response, patch, directory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            retain = true;
            if (created)
            {
                notes?.Report($"interrupted worker for {path} retained at {directory}");
            }

            throw;
        }
        catch (Exception exception) when (created && !retain)
        {
            retain = true;
            throw new InvalidOperationException($"worker for {path} failed: {exception.Message}; no changes were applied; worker retained at {directory}", exception);
        }
        finally
        {
            if (created && !retain)
            {
                await RemoveAsync(directory).ConfigureAwait(false);
            }
        }
    }

    public async Task ReleaseAsync(FileFixEdit edit, CancellationToken cancellationToken)
    {
        if (edit.Worktree is { } directory)
        {
            await RemoveAsync(directory).ConfigureAwait(false);
        }
    }

    private async Task RemoveAsync(string directory)
    {
        await worktrees.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await GitProcess.RunAsync(repoRoot, ["worktree", "remove", "--force", directory], null, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            worktrees.Release();
        }
    }

    public void Dispose() => worktrees.Dispose();
}
