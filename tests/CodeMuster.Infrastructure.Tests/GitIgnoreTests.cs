using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public sealed class GitIgnoreTests : IDisposable
{
    private const string Ledger = ".codemuster/ledger.db";

    private readonly TempRepo _repo = new();
    private readonly ISourceTree _tree;

    public GitIgnoreTests()
    {
        _repo.WriteFile("src/a.cs", "class A {}\n");
        _repo.Commit("first");
        _tree = new GitSourceTree(_repo.Root);
    }

    [Theory]
    [InlineData("bin/\n.codemuster/ledger.db\n", true)]
    [InlineData("/.codemuster/ledger.db\n", true)]
    [InlineData(".codemuster/ledger.db  \r\nobj/\r\n", true)]
    [InlineData(".codemuster/\n", true)]
    [InlineData("**/ledger.db\n", true)]
    [InlineData("bin/\n", false)]
    [InlineData("  .codemuster/ledger.db\n", false)]
    [InlineData(".codemuster/ledger.db\n!.codemuster/ledger.db\n", false)]
    public async Task IsIgnored_FollowsTheRepoGitignore(string gitignore, bool expected)
    {
        _repo.WriteFile(".gitignore", gitignore);

        Assert.Equal(expected, await _tree.IsIgnoredAsync(Ledger, CancellationToken.None));
    }

    [Fact]
    public async Task IsIgnored_IsFalse_WithoutAnyGitignore()
    {
        Assert.False(await _tree.IsIgnoredAsync(Ledger, CancellationToken.None));
    }

    [Fact]
    public async Task IsIgnored_IgnoresRulesFromGitInfoExclude()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo.Root, ".git", "info", "exclude"), ".codemuster/ledger.db\n");

        Assert.False(await _tree.IsIgnoredAsync(Ledger, CancellationToken.None));
    }

    [Fact]
    public async Task IsIgnored_HonoursNestedGitignore()
    {
        _repo.WriteFile(".codemuster/.gitignore", "ledger.db\n");

        Assert.True(await _tree.IsIgnoredAsync(Ledger, CancellationToken.None));
    }

    public void Dispose() => _repo.Dispose();
}
