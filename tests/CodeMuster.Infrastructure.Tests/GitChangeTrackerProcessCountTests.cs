using System.Diagnostics;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

/// <summary>Counts the processes a snapshot starts, so it runs alone: a parallel test's git calls would be counted too.</summary>
[Collection(ProcessEnvironment.Name)]
public sealed class GitChangeTrackerProcessCountTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Untracked_files_never_count_as_changes_and_many_of_them_start_only_a_few_git_processes()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        for (var i = 0; i < 500; i++)
        {
            repo.WriteFile($"notes/n{i}.cs", $"class N{i} {{}}\n");
        }

        var changes = new GitChangeTracker(repo.Root);
        var before = ChildProcesses.StartedCount;
        var watch = Stopwatch.StartNew();
        var snapshot = await changes.SnapshotAsync(None);
        watch.Stop();
        var started = ChildProcesses.StartedCount - before;
        await changes.AcknowledgeAsync(snapshot, None);
        repo.WriteFile("audit.md", "# CodeMuster report\n");
        repo.WriteFile("notes/n1.cs", "class Renamed {}\n");

        Assert.False((await changes.ChangesAsync(None)).Detected);
        // One process per untracked file would start 500; the snapshot needs a handful whatever the runner's speed.
        Assert.True(started <= 10, $"the snapshot of 500 untracked files started {started} processes");
        // Only a backstop: a slow CI runner once took 7.7 s for a few git calls that take 0.7 s locally.
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), $"the snapshot of 500 untracked files took {watch.Elapsed}");
    }
}
