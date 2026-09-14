using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public sealed class GitFileFixerTests : IDisposable
{
    private readonly TempRepo repo = new();

    public GitFileFixerTests()
    {
        repo.WriteFile("a.cs", "class A { }\n");
        repo.WriteFile("b.cs", "class B { }\n");
        repo.Commit("fixture");
    }


    [Fact]
    public async Task ExplicitRelatedFileScope_AcceptsAndCommitsTheCoherentRepair()
    {
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((pack, _) =>
        {
            Assert.Contains("a.cs, b.cs", pack);
            File.WriteAllText(Path.Combine(dir, "a.cs"), "class A { int x; }\n");
            File.WriteAllText(Path.Combine(dir, "b.cs"), "class B { int x; }\n");
            return Task.FromResult("response");
        }), relatedFiles: ["b.cs"]);
        var edit = await fixer.RunAsync("a.cs", "pack", CancellationToken.None);
        var workspace = new GitWorkspace(repo.Root);
        await workspace.ApplyPatchAsync(edit.Patch, CancellationToken.None);
        await workspace.CommitFilesAsync(["a.cs", "b.cs"], "coherent repair", CancellationToken.None);
        Assert.Equal(new[] { "a.cs", "b.cs" }, repo.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        Assert.True(await workspace.IsCleanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WorkerEditsAreIsolated_AndOnlyItsAssignedFileIsAppliedAndCommitted()
    {
        var worktrees = repo.Run("worktree", "list", "--porcelain");
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((_, _) =>
        {
            File.WriteAllText(Path.Combine(dir, "a.cs"), "class A { int x; }\n");
            Assert.Equal("class A { }\n", File.ReadAllText(Path.Combine(repo.Root, "a.cs")));
            return Task.FromResult("response");
        }));

        var edit = await fixer.RunAsync("a.cs", "pack", CancellationToken.None);

        Assert.Equal("response", edit.Response);
        Assert.Equal(worktrees, repo.Run("worktree", "list", "--porcelain"));
        var workspace = new GitWorkspace(repo.Root);
        await workspace.ApplyPatchAsync(edit.Patch, CancellationToken.None);
        repo.WriteFile("b.cs", "other work\n");
        await workspace.CommitFileAsync("a.cs", "fix a", CancellationToken.None);
        Assert.Equal("a.cs", repo.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.Equal("other work\n", File.ReadAllText(Path.Combine(repo.Root, "b.cs")));
        Assert.True(await workspace.HasFileChangesAsync("b.cs", CancellationToken.None));
    }

    [Theory]
    [InlineData("b.cs")]
    [InlineData("new.cs")]
    public async Task WorkerThatChangesAnotherFile_IsRejectedWithoutChangingTheMainCheckout(string other)
    {
        string? worker = null;
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((_, _) =>
        {
            worker = dir;
            File.WriteAllText(Path.Combine(dir, other), "wrong file\n");
            return Task.FromResult("response");
        }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixer.RunAsync("a.cs", "pack", CancellationToken.None));

        Assert.Contains("outside its assigned file", error.Message);
        Assert.Empty(repo.Run("status", "--porcelain"));
        Assert.NotNull(worker);
        repo.Run("worktree", "remove", "--force", worker);
    }

    [Fact]
    public async Task UntrackedHookCache_DoesNotBlockTheAssignedPatch()
    {
        var originalWorktrees = repo.Run("worktree", "list", "--porcelain");
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((_, _) =>
        {
            File.WriteAllText(Path.Combine(dir, "a.cs"), "fixed\n");
            Directory.CreateDirectory(Path.Combine(dir, ".impeccable"));
            File.WriteAllText(Path.Combine(dir, ".impeccable", "hook.cache.json"), "{}");
            return Task.FromResult("response");
        }));

        var edit = await fixer.RunAsync("a.cs", "pack", CancellationToken.None);

        Assert.Contains("+fixed", edit.Patch);
        Assert.DoesNotContain("hook.cache", edit.Patch);
        Assert.Equal(originalWorktrees, repo.Run("worktree", "list", "--porcelain"));
        Assert.Empty(repo.Run("status", "--porcelain"));
    }

    [Theory]
    [InlineData("b.cs", true)]
    [InlineData("new.cs", false)]
    [InlineData(".impeccable/hook.cache.json", true)]
    [InlineData(".impeccable/other.json", false)]
    public async Task RejectedEdits_NameTheExtraPath_AndRetainTheWorker(string other, bool tracked)
    {
        if (tracked)
        {
            repo.WriteFile(other, "original\n");
            repo.Commit("track extra file");
        }

        string? worker = null;
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((_, _) =>
        {
            worker = dir;
            var extra = Path.Combine(dir, other);
            Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
            File.WriteAllText(extra, "extra edit\n");
            return Task.FromResult("response");
        }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixer.RunAsync("a.cs", "pack", CancellationToken.None));

        Assert.Contains(other, error.Message);
        Assert.NotNull(worker);
        Assert.Contains(worker, error.Message);
        Assert.Equal("extra edit\n", File.ReadAllText(Path.Combine(worker, other)));
        Assert.Empty(repo.Run("status", "--porcelain"));
        repo.Run("worktree", "remove", "--force", worker);
    }

    [Fact]
    public async Task TwoWorkers_EditDifferentFilesConcurrently_WithoutSeeingEachOthersEdits()
    {
        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent(async (pack, ct) =>
        {
            var isA = pack.StartsWith("a", StringComparison.Ordinal);
            var path = isA ? "a.cs" : "b.cs";
            File.WriteAllText(Path.Combine(dir, path), "fixed\n");
            (isA ? aStarted : bStarted).TrySetResult();
            await (isA ? bStarted : aStarted).Task.WaitAsync(ct);
            Assert.Contains("class", File.ReadAllText(Path.Combine(dir, isA ? "b.cs" : "a.cs")));
            return "response";
        }));

        var edits = await Task.WhenAll(fixer.RunAsync("a.cs", "a", timeout.Token), fixer.RunAsync("b.cs", "b", timeout.Token));

        Assert.All(edits, edit => Assert.Contains("+fixed", edit.Patch));
        Assert.Empty(repo.Run("status", "--porcelain"));
    }

    [Fact]
    public async Task FileRollback_DoesNotTouchAnotherFile()
    {
        repo.WriteFile("a.cs", "failed fix\n");
        repo.WriteFile("b.cs", "keep me\n");
        repo.Run("add", "a.cs", "b.cs");
        var workspace = new GitWorkspace(repo.Root);

        await workspace.RestoreFileAsync("a.cs", CancellationToken.None);

        Assert.False(await workspace.HasFileChangesAsync("a.cs", CancellationToken.None));
        Assert.Equal("keep me\n", File.ReadAllText(Path.Combine(repo.Root, "b.cs")));
        Assert.Equal("b.cs", repo.Run("diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task Cancellation_KeepsTheInterruptedWorkerForRecovery()
    {
        string? worker = null;
        using var cancellation = new CancellationTokenSource();
        var notes = new List<string>();
        using var fixer = new GitFileFixer(repo.Root, dir => new CallbackAgent((_, _) =>
        {
            worker = dir;
            File.WriteAllText(Path.Combine(dir, "a.cs"), "unfinished\n");
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }), new Notes(notes));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixer.RunAsync("a.cs", "pack", cancellation.Token));

        Assert.NotNull(worker);
        Assert.Equal("unfinished\n", File.ReadAllText(Path.Combine(worker, "a.cs")));
        Assert.Contains(notes, note => note.Contains(worker, StringComparison.Ordinal));
        Assert.Empty(repo.Run("status", "--porcelain"));
        repo.Run("worktree", "remove", "--force", worker);
    }

    private sealed class Notes(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    private sealed class CallbackAgent(Func<string, CancellationToken, Task<string>> run) : IAgentAdapter
    {
        public AgentIdentity Identity { get; } = new("fake", null, null);
        public Task<string> RunAsync(string pack, CancellationToken cancellationToken) => run(pack, cancellationToken);
    }

    public void Dispose() => repo.Dispose();
}
