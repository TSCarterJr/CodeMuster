using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeWorkspace : IWorkspace
{
    public bool Clean { get; set; } = true;
    public List<string> Commits { get; } = [];
    public int Restores { get; private set; }
    public int Stashes { get; private set; }
    public string? RestoredStash { get; private set; }
    public CancellationToken RestoreStashToken { get; private set; }
    public List<string> CommittedFiles { get; } = [];
    public List<string> RestoredFiles { get; } = [];

    public Task ApplyPatchAsync(string patch, CancellationToken cancellationToken)
    {
        Clean = patch.Length == 0;
        return Task.CompletedTask;
    }

    public Task<bool> HasFileChangesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(!Clean);

    public Task CommitFileAsync(string path, string message, CancellationToken cancellationToken)
    {
        CommittedFiles.Add(path);
        return CommitAsync(message, cancellationToken);
    }

    public Task CommitFilesAsync(IReadOnlyList<string> paths, string message, CancellationToken cancellationToken)
    {
        CommittedFiles.AddRange(paths);
        return CommitAsync(message, cancellationToken);
    }

    public Task RestoreFileAsync(string path, CancellationToken cancellationToken)
    {
        RestoredFiles.Add(path);
        Clean = true;
        return Task.CompletedTask;
    }

    public Task<string> StashAsync(CancellationToken cancellationToken)
    {
        Stashes++;
        Clean = true;
        return Task.FromResult("saved-stash");
    }

    public Task RestoreStashAsync(string stash, CancellationToken cancellationToken)
    {
        RestoredStash = stash;
        RestoreStashToken = cancellationToken;
        Clean = false;
        return Task.CompletedTask;
    }

    public Task<bool> IsCleanAsync(CancellationToken cancellationToken) => Task.FromResult(Clean);

    public Task CommitAsync(string message, CancellationToken cancellationToken)
    {
        Commits.Add(message);
        Clean = true;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken cancellationToken)
    {
        Restores++;
        Clean = true;
        return Task.CompletedTask;
    }
}
