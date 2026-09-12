using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeWorkspace : IWorkspace
{
    public bool Clean { get; set; } = true;
    public List<string> Commits { get; } = [];
    public int Restores { get; private set; }

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
