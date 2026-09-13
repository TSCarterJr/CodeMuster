using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GitFileFixer(string repoRoot, Func<string, IAgentAdapter> adapterForDirectory, IProgress<string>? notes = null) : IFileFixer, IDisposable
{
    private readonly SemaphoreSlim worktrees = new(1, 1);

    public async Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
    {
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
            var instructions = pack + $"\n\nYou are working in an isolated worktree. Edit only {path}. Do not commit, stage, or modify any other file. The coordinator runs tests and commits your file.\n";
            var response = await adapterForDirectory(directory).RunAsync(instructions, cancellationToken).ConfigureAwait(false);
            var changed = await GitProcess.RunAsync(directory, ["diff", "--name-only", "-z", baseline], null, cancellationToken).ConfigureAwait(false);
            var untracked = await GitProcess.RunAsync(directory, ["ls-files", "--others", "--exclude-standard", "-z"], null, cancellationToken).ConfigureAwait(false);
            if (changed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(changedPath => changedPath != path) || untracked.Length > 0)
            {
                throw new InvalidOperationException($"worker for {path} changed files outside its assigned file; no changes were applied");
            }

            var patch = await GitProcess.RunAsync(directory, ["--literal-pathspecs", "diff", "--binary", baseline, "--", path], null, cancellationToken).ConfigureAwait(false);
            return new FileFixEdit(response, patch);
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
        finally
        {
            if (created && !retain)
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
        }
    }

    public void Dispose() => worktrees.Dispose();
}
