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

    public void Dispose() => _repo.Dispose();
}
