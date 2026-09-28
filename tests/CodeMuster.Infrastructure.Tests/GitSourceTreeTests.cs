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
        Assert.Equal(_repo.Run("hash-object", "src/a/staged.cs").Trim(), staged.KnownHash);
        Assert.NotEqual(_repo.Run("rev-parse", "HEAD:src/a/staged.cs").Trim(), staged.KnownHash);
    }

    [Fact]
    public async Task LastCommits_match_git_log_per_path()
    {
        var paths = (await _tree.ListFilesAsync(CancellationToken.None)).Select(f => f.Path).ToList();

        var commits = await _tree.LastCommitsAsync(paths, CancellationToken.None);

        foreach (var path in paths)
        {
            var sha = NullIfEmpty(_repo.Run("log", "-1", "--format=%H", "--", path));
            Assert.Equal(sha, commits.GetValueOrDefault(path)?.Sha);
            Assert.Equal(NullIfEmpty(_repo.Run("log", "-1", "--format=%cI", "--", path)), commits.GetValueOrDefault(path)?.At);
        }

        Assert.Equal(_repo.Run("rev-parse", "HEAD").Trim(), commits["src/a/b.cs"].Sha);
        Assert.Equal(_repo.Run("rev-list", "--max-parents=0", "HEAD").Trim(), commits["gen/out.cs"].Sha);
        Assert.False(commits.ContainsKey("docs/new name.md"));
    }

    [Fact]
    public async Task LastCommits_answers_only_the_paths_asked_for()
    {
        var commits = await _tree.LastCommitsAsync(["src/a/b.cs", "src/c/naïve.cs"], CancellationToken.None);

        Assert.Equal(["src/a/b.cs", "src/c/naïve.cs"], commits.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Listing_reads_no_history_so_it_works_when_history_cannot_be_read_while_LastCommits_throws()
    {
        // Losing the root commit's object leaves the index, HEAD and its tree readable, but git log cannot walk past it (D75).
        var root = _repo.Run("rev-list", "--max-parents=0", "HEAD").Trim();
        var loose = new FileInfo(Path.Combine(_repo.Root, ".git", "objects", root[..2], root[2..]));
        loose.Attributes = FileAttributes.Normal;
        loose.Delete();

        var files = await _tree.ListFilesAsync(CancellationToken.None);

        Assert.Equal(8, files.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tree.LastCommitsAsync(files.Select(f => f.Path).ToList(), CancellationToken.None));
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

    [Fact]
    public async Task ReadFileAtCommit_returns_the_committed_content_or_null_when_the_commit_lacks_the_file()
    {
        var first = _repo.Run("rev-list", "--max-parents=0", "HEAD").Trim();

        Assert.Equal("class B {}\n", await _tree.ReadFileAtCommitAsync(first, "src/a/b.cs", CancellationToken.None));
        Assert.Equal("class B { int X; }\n", await _tree.ReadFileAtCommitAsync("HEAD", "src/a/b.cs", CancellationToken.None));
        Assert.Equal("class Naive {}\n", await _tree.ReadFileAtCommitAsync("HEAD", "src/c/naïve.cs", CancellationToken.None));
        Assert.Null(await _tree.ReadFileAtCommitAsync(first, "src/c/naïve.cs", CancellationToken.None));
    }

    [Fact]
    public async Task ChangedSince_lists_committed_changes_between_the_ref_and_HEAD_only()
    {
        Assert.Equal(["src/a/b.cs"], await _tree.ChangedSinceAsync("HEAD~1", CancellationToken.None));
        Assert.Equal(
            ["docs/old name.md", "docs/read me.md", "src/a/b.cs", "src/a/staged.cs", "src/c/naïve.cs"],
            (await _tree.ChangedSinceAsync("HEAD~2", CancellationToken.None)).Order(StringComparer.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tree.ChangedSinceAsync("no-such-ref", CancellationToken.None));
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
