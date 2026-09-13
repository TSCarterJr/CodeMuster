using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public sealed class GitWorkspaceTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly IWorkspace _workspace;

    public GitWorkspaceTests()
    {
        _repo.WriteFile("src/a.cs", "class A { }\n");
        _repo.Commit("first");
        _workspace = new GitWorkspace(_repo.Root);
    }

    [Fact]
    public async Task AFreshCommit_IsClean_AndAnEditIsNot()
    {
        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));

        _repo.WriteFile("src/a.cs", "class A { int x; }\n");

        Assert.False(await _workspace.IsCleanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Commit_RecordsTheChange_AndLeavesTheTreeClean()
    {
        _repo.WriteFile("src/a.cs", "class A { int x; }\n");

        await _workspace.CommitAsync("fix src/a.cs\n\nAdded the field.", CancellationToken.None);

        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
        Assert.Contains("fix src/a.cs", _repo.Run("log", "-1", "--format=%s"));
        Assert.Contains("Added the field.", _repo.Run("log", "-1", "--format=%b"));
    }

    [Fact]
    public async Task Restore_ThrowsAwayUncommittedEdits()
    {
        _repo.WriteFile("src/a.cs", "class A { int broken }\n");

        await _workspace.RestoreAsync(CancellationToken.None);

        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
        Assert.Equal("class A { }\n", File.ReadAllText(Path.Combine(_repo.Root, "src", "a.cs")).Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_LeavesUntrackedFilesOutsideTheCommit(bool deleteTrackedFile)
    {
        const string untracked = ".claude/agents/local agent.md";
        const string content = "local work\n";
        _repo.WriteFile(untracked, content);
        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
        if (deleteTrackedFile)
        {
            File.Delete(Path.Combine(_repo.Root, "src", "a.cs"));
        }
        else
        {
            _repo.WriteFile("src/a.cs", "class A { int x; }\n");
        }

        await _workspace.CommitAsync("fix src/a.cs", CancellationToken.None);

        Assert.Equal("src/a.cs", _repo.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.Equal(untracked, _repo.Run("ls-files", "--others", "--exclude-standard").Trim());
        Assert.Equal(content, File.ReadAllText(Path.Combine(_repo.Root, untracked)));
        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stash_RestoresBothIndexAndWorktree_AndLeavesUntrackedFilesAndOlderStashesAlone()
    {
        _repo.WriteFile("src/a.cs", "older stash\n");
        _repo.Run("stash", "push", "-m", "older work");
        var older = _repo.Run("rev-parse", "refs/stash").Trim();
        _repo.WriteFile("src/a.cs", "staged\n");
        _repo.Run("add", "src/a.cs");
        _repo.WriteFile("src/a.cs", "unstaged\n");
        _repo.WriteFile(".codemuster/config.json", "{}\n");
        var index = _repo.Run("diff", "--cached");
        var worktree = _repo.Run("diff");

        var stash = await _workspace.StashAsync(CancellationToken.None);

        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
        Assert.NotEqual(older, stash);
        Assert.Equal("{}\n", File.ReadAllText(Path.Combine(_repo.Root, ".codemuster", "config.json")));
        await _workspace.RestoreStashAsync(stash, CancellationToken.None);

        Assert.Equal(index, _repo.Run("diff", "--cached"));
        Assert.Equal(worktree, _repo.Run("diff"));
        Assert.Equal(new[] { stash, older }, _repo.Run("stash", "list", "--format=%H").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task RestoreStash_SavesInterruptedFixEditsBeforeRestoringLocalWork()
    {
        _repo.WriteFile("src/a.cs", "local work\n");
        var stash = await _workspace.StashAsync(CancellationToken.None);
        _repo.WriteFile("src/a.cs", "unfinished fix\n");

        await _workspace.RestoreStashAsync(stash, CancellationToken.None);

        Assert.Equal("local work\n", File.ReadAllText(Path.Combine(_repo.Root, "src", "a.cs")));
        Assert.Equal("unfinished fix", _repo.Run("show", "refs/stash:src/a.cs").Trim());
        Assert.Contains(stash, _repo.Run("stash", "list", "--format=%H"));
    }

    [Fact]
    public async Task RestoreStash_ConflictKeepsTheBackupAndFixCommit_AndNamesTheRecoveryCommand()
    {
        _repo.WriteFile("src/a.cs", "local work\n");
        var stash = await _workspace.StashAsync(CancellationToken.None);
        _repo.WriteFile("src/a.cs", "committed fix\n");
        var head = _repo.Commit("fix");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _workspace.RestoreStashAsync(stash, CancellationToken.None));

        Assert.Contains("git stash apply --index " + stash, error.Message);
        Assert.Equal(head.Trim(), _repo.Run("rev-parse", "HEAD").Trim());
        Assert.Equal("local work", _repo.Run("show", stash + ":src/a.cs").Trim());
        Assert.Contains(stash, _repo.Run("stash", "list", "--format=%H"));
    }

    [Fact]
    public async Task Stash_WhenClean_DoesNotReuseAnOlderStash()
    {
        _repo.WriteFile("src/a.cs", "older stash\n");
        _repo.Run("stash", "push", "-m", "older work");
        var older = _repo.Run("rev-parse", "refs/stash");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _workspace.StashAsync(CancellationToken.None));

        Assert.Equal(older, _repo.Run("rev-parse", "refs/stash"));
        Assert.True(await _workspace.IsCleanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stash_WhenOnlyASubmoduleIsDirty_DoesNotReturnAnOlderStash()
    {
        using var child = new TempRepo();
        child.WriteFile("child.cs", "original\n");
        child.Commit("child");
        _repo.Run("-c", "protocol.file.allow=always", "submodule", "add", child.Root, "child");
        _repo.Commit("submodule");
        _repo.WriteFile("src/a.cs", "older stash\n");
        _repo.Run("stash", "push", "-m", "older work");
        var older = _repo.Run("rev-parse", "refs/stash");
        _repo.WriteFile("child/child.cs", "local submodule work\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _workspace.StashAsync(CancellationToken.None));

        Assert.Equal(older, _repo.Run("rev-parse", "refs/stash"));
        Assert.Equal("local submodule work\n", File.ReadAllText(Path.Combine(_repo.Root, "child", "child.cs")));
    }

    public void Dispose() => _repo.Dispose();
}
