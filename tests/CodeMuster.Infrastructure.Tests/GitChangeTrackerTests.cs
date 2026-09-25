using System.Diagnostics;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class GitChangeTrackerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ReadOnlyToolsDoNotDirtyCoverage_AndScanDoesNotLoseConcurrentEdits()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root);
        var snapshot = await changes.SnapshotAsync(None);
        await changes.AcknowledgeAsync(snapshot, None);
        Assert.False((await changes.ChangesAsync(None)).Detected);
        await changes.NotifyAsync(None);
        Assert.False((await changes.ChangesAsync(None)).Detected);
        repo.WriteFile("a.cs", "class A { int n; }\n");
        await changes.NotifyAsync(None);
        await changes.AcknowledgeAsync(snapshot, None);
        Assert.Equal(["a.cs"], (await changes.ChangesAsync(None)).Paths);
        var fresh = await changes.SnapshotAsync(None);
        await changes.AcknowledgeAsync(fresh, None);
        Assert.False((await changes.ChangesAsync(None)).Detected);
    }

    [Fact]
    public async Task Before_any_scan_only_a_notification_counts_and_names_nothing()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root);
        repo.WriteFile("a.cs", "class A { int n; }\n");
        Assert.False((await changes.ChangesAsync(None)).Detected);

        await changes.NotifyAsync(None);

        var notified = await changes.ChangesAsync(None);
        Assert.True(notified.Detected);
        Assert.Empty(notified.Paths);
    }

    [Fact]
    public async Task A_snapshot_stored_by_an_older_version_falls_back_to_the_notification()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root);
        await changes.AcknowledgeAsync(new string('a', 64), None);
        Assert.False((await changes.ChangesAsync(None)).Detected);

        await changes.NotifyAsync(None);

        var fallback = await changes.ChangesAsync(None);
        Assert.True(fallback.Detected);
        Assert.Empty(fallback.Paths);
    }

    [Fact]
    public async Task Only_files_a_scan_would_review_count_and_each_one_is_named()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.WriteFile("b.cs", "class B {}\n");
        repo.WriteFile("gone.cs", "class Gone {}\n");
        repo.WriteFile("README.md", "# readme\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root, path => path.EndsWith(".cs", StringComparison.Ordinal));
        await changes.AcknowledgeAsync(await changes.SnapshotAsync(None), None);

        repo.WriteFile("README.md", "# edited\n");
        Assert.False((await changes.ChangesAsync(None)).Detected);

        repo.WriteFile("b.cs", "class B { int n; }\n");
        File.Delete(Path.Combine(repo.Root, "gone.cs"));
        repo.WriteFile("added.cs", "class Added {}\n");
        repo.Run("add", "added.cs");

        Assert.Equal(["added.cs", "b.cs", "gone.cs"], (await changes.ChangesAsync(None)).Paths);
    }

    [Fact]
    public async Task Staging_or_committing_the_scanned_content_is_not_a_change()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "true");
        repo.WriteFile("a.cs", "class A {}\r\n");
        repo.Commit("seed");
        repo.WriteFile("a.cs", "class A {\r\n    int n;\r\n}\r\n");
        var changes = new GitChangeTracker(repo.Root);
        await changes.AcknowledgeAsync(await changes.SnapshotAsync(None), None);

        repo.Run("add", "a.cs");
        Assert.False((await changes.ChangesAsync(None)).Detected);

        repo.Commit("edit");
        Assert.False((await changes.ChangesAsync(None)).Detected);
    }

    [Fact]
    public async Task Staging_the_scanned_edit_of_a_file_committed_with_crlf_is_not_a_change()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\r\n");
        repo.Commit("seed with CRLF");
        repo.Run("config", "core.autocrlf", "true");
        repo.WriteFile("a.cs", "class A {\r\n    int n;\r\n}\r\n");
        var changes = new GitChangeTracker(repo.Root);
        await changes.AcknowledgeAsync(await changes.SnapshotAsync(None), None);

        repo.Run("add", "a.cs");
        Assert.Equal("class A {\r\n    int n;\r\n}\r\n", repo.Run("show", ":a.cs"));
        Assert.False((await changes.ChangesAsync(None)).Detected);

        repo.Commit("edit");
        Assert.False((await changes.ChangesAsync(None)).Detected);
    }

    [Fact]
    public async Task An_untracked_nested_repository_or_worktree_does_not_break_the_snapshot()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        repo.WriteFile("vendorlib/lib.cs", "class L {}\n");
        repo.Run("-C", "vendorlib", "init", "-q");
        repo.Run("worktree", "add", "-q", "-b", "feature", ".claude/worktrees/feature");
        var untracked = repo.Run("ls-files", "--others", "--exclude-standard");
        Assert.Contains("vendorlib/", untracked);
        Assert.Contains(".claude/worktrees/feature/", untracked);
        var changes = new GitChangeTracker(repo.Root);

        await changes.AcknowledgeAsync(await changes.SnapshotAsync(None), None);
        await changes.NotifyAsync(None);

        Assert.False((await changes.ChangesAsync(None)).Detected);
    }

    [Fact]
    public async Task Untracked_files_never_count_as_changes_and_many_of_them_stay_cheap()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        for (var i = 0; i < 500; i++)
        {
            repo.WriteFile($"notes/n{i}.cs", $"class N{i} {{}}\n");
        }

        var changes = new GitChangeTracker(repo.Root);
        var watch = Stopwatch.StartNew();
        var snapshot = await changes.SnapshotAsync(None);
        watch.Stop();
        await changes.AcknowledgeAsync(snapshot, None);
        repo.WriteFile("audit.md", "# CodeMuster report\n");
        repo.WriteFile("notes/n1.cs", "class Renamed {}\n");

        Assert.False((await changes.ChangesAsync(None)).Detected);
        // One process per untracked file took about 25 ms each on Windows; a few git calls take well under a second.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the snapshot of 500 untracked files took {watch.Elapsed}");
    }

    [Fact]
    public async Task Concurrent_notifications_never_fail_or_leave_temporary_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.cs", "class A {}\n");
        repo.Commit("seed");
        var changes = new GitChangeTracker(repo.Root);

        for (var round = 0; round < 4; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => changes.NotifyAsync(None))));
        }

        Assert.Equal(["changed"], Directory.GetFiles(Path.Combine(repo.Root, ".git", "codemuster")).Select(Path.GetFileName));
    }
}
