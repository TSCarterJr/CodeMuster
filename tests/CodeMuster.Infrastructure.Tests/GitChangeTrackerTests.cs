using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class GitChangeTrackerTests
{
    [Fact]
    public async Task ReadOnlyToolsDoNotDirtyCoverage_AndScanDoesNotLoseConcurrentEdits()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root);
        var snapshot = await changes.SnapshotAsync(CancellationToken.None);
        await changes.AcknowledgeAsync(snapshot, CancellationToken.None);
        Assert.False(await changes.HasChangesAsync(CancellationToken.None));
        await changes.NotifyAsync(CancellationToken.None);
        Assert.False(await changes.HasChangesAsync(CancellationToken.None));
        repo.WriteFile("a.cs", "class A { int n; }\n");
        await changes.NotifyAsync(CancellationToken.None);
        await changes.AcknowledgeAsync(snapshot, CancellationToken.None);
        Assert.True(await changes.HasChangesAsync(CancellationToken.None));
        var fresh = await changes.SnapshotAsync(CancellationToken.None);
        await changes.AcknowledgeAsync(fresh, CancellationToken.None);
        Assert.False(await changes.HasChangesAsync(CancellationToken.None));
    }
}
