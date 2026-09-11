using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public sealed class GitSourceTreeTests : IDisposable
{
    private const string ModifiedReadme = "# readme, edited in the working tree\n";
    private const string StagedContent = "class Staged { int Y; }\n";

    private readonly TempRepo _repo = new();
    private readonly ISourceTree _tree;

    public GitSourceTreeTests()
    {
        _repo.WriteFile(".gitattributes", "gen/* linguist-generated\nvendor/* linguist-generated=true\n");
        _repo.WriteFile("src/a/b.cs", "class B {}\n");
        _repo.WriteFile("gen/out.cs", "// generated\n");
        _repo.WriteFile("vendor/lib.js", "var x;\n");
        _repo.WriteFile("tmp/gone.txt", "bye\n");
        _repo.Commit("first");

        _repo.WriteFile("docs/read me.md", "# readme\n");
        _repo.WriteFile("src/c/naïve.cs", "class Naive {}\n");
        _repo.WriteFile("src/a/staged.cs", "class Staged {}\n");
        _repo.WriteFile("docs/old name.md", "move me\n");
        _repo.Commit("second");

        _repo.WriteFile("src/a/b.cs", "class B { int X; }\n");
        _repo.Commit("third");

        _repo.WriteFile("docs/read me.md", ModifiedReadme);
        _repo.WriteFile("src/a/staged.cs", StagedContent);
        _repo.Run("add", "src/a/staged.cs");
        _repo.Run("mv", "docs/old name.md", "docs/new name.md");
        _repo.WriteFile("docs/new name.md", "moved and edited\n");
        File.Delete(Path.Combine(_repo.Root, "tmp/gone.txt"));

        _tree = new GitSourceTree(_repo.Root);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task ListFiles_returns_every_tracked_file_on_disk_with_forward_slash_paths()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        Assert.Equal(
            [".gitattributes", "docs/new name.md", "docs/read me.md", "gen/out.cs", "src/a/b.cs", "src/a/staged.cs", "src/c/naïve.cs", "vendor/lib.js"],
            files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.All(files, f => Assert.DoesNotContain('\\', f.Path));
    }

    [Fact]
    public async Task Clean_files_carry_the_index_blob_sha_as_KnownHash()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        foreach (var path in new[] { ".gitattributes", "src/a/b.cs", "gen/out.cs", "src/c/naïve.cs", "vendor/lib.js" })
        {
            var expected = _repo.Run("rev-parse", $"HEAD:{path}").Trim();
            Assert.Equal(expected, files.Single(f => f.Path == path).KnownHash);
        }
    }

    [Fact]
    public async Task Modified_unstaged_file_has_null_KnownHash()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        Assert.Null(files.Single(f => f.Path == "docs/read me.md").KnownHash);
        Assert.Null(files.Single(f => f.Path == "docs/new name.md").KnownHash);
    }

    [Fact]
    public async Task Staged_modification_reports_the_new_index_sha()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        var staged = files.Single(f => f.Path == "src/a/staged.cs");
        Assert.Equal(GitBlobHasher.Hash(Encoding.UTF8.GetBytes(StagedContent)), staged.KnownHash);
        Assert.NotEqual(_repo.Run("rev-parse", "HEAD:src/a/staged.cs").Trim(), staged.KnownHash);
    }

    [Fact]
    public async Task LastCommit_and_LastCommitAt_match_git_log_per_path()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        foreach (var file in files)
        {
            Assert.Equal(NullIfEmpty(_repo.Run("log", "-1", "--format=%H", "--", file.Path)), file.LastCommit);
            Assert.Equal(NullIfEmpty(_repo.Run("log", "-1", "--format=%cI", "--", file.Path)), file.LastCommitAt);
        }

        Assert.Equal(_repo.Run("rev-parse", "HEAD").Trim(), files.Single(f => f.Path == "src/a/b.cs").LastCommit);
        Assert.Null(files.Single(f => f.Path == "docs/new name.md").LastCommit);
    }

    [Fact]
    public async Task LinguistGenerated_follows_committed_gitattributes()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        Assert.True(files.Single(f => f.Path == "gen/out.cs").LinguistGenerated);
        Assert.True(files.Single(f => f.Path == "vendor/lib.js").LinguistGenerated);
        Assert.All(files.Where(f => f.Path is not ("gen/out.cs" or "vendor/lib.js")), f => Assert.False(f.LinguistGenerated));
    }

    [Fact]
    public async Task Size_and_Mtime_come_from_the_working_tree_file()
    {
        var files = await _tree.ListFilesAsync(CancellationToken.None);

        var readme = files.Single(f => f.Path == "docs/read me.md");
        var info = new FileInfo(Path.Combine(_repo.Root, "docs", "read me.md"));
        Assert.Equal(Encoding.UTF8.GetByteCount(ModifiedReadme), readme.Size);
        Assert.Equal(Timestamps.Format(new DateTimeOffset(info.LastWriteTimeUtc)), readme.Mtime);
    }

    [Fact]
    public async Task HeadCommit_matches_git_rev_parse()
    {
        Assert.Equal(_repo.Run("rev-parse", "HEAD").Trim(), await _tree.HeadCommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadFile_returns_working_tree_content_not_committed_content()
    {
        Assert.Equal(ModifiedReadme, await _tree.ReadFileAsync("docs/read me.md", CancellationToken.None));
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
