namespace CodeMuster.Infrastructure.Tests;

public sealed class GitTopLevelTests : IDisposable
{
    private readonly TempRepo _repo = new();

    [Fact]
    public async Task FindTopLevel_FromSubdirectory_ReturnsRepoRoot()
    {
        _repo.WriteFile("src/a/b.cs", "class B {}\n");
        _repo.Commit("first");

        var found = await GitSourceTree.FindTopLevelAsync(Path.Combine(_repo.Root, "src", "a"), CancellationToken.None);

        Assert.Equal(Path.GetFileName(_repo.Root), Path.GetFileName(found));
        Assert.True(Directory.Exists(Path.Combine(found, ".git")));
        Assert.True(File.Exists(Path.Combine(found, "src", "a", "b.cs")));
    }

    [Fact]
    public async Task FindTopLevel_OutsideRepo_Throws()
    {
        var outside = Path.Combine(Path.GetTempPath(), "codemuster-tests", "not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => GitSourceTree.FindTopLevelAsync(outside, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(outside);
        }
    }

    public void Dispose() => _repo.Dispose();
}
