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

    public void Dispose() => _repo.Dispose();
}
