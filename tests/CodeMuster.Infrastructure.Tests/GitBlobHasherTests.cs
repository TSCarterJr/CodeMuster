using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class GitBlobHasherTests
{
    [Fact]
    public async Task Hashes_every_modified_path_in_one_call_as_git_add_then_records_it_and_leaves_the_index_alone()
    {
        using var repo = new TempRepo();
        List<string> paths = ["hello.txt", "empty.txt", "src/a/crlf.cs", "docs/read me.md", "src/c/naïve.cs", "-leading-dash.txt", "#hash.txt"];
        if (!OperatingSystem.IsWindows())
        {
            // Names Windows cannot hold: each would break a plain one-path-per-line list.
            paths.AddRange(["\"leading-quote.txt", "quote\"d.txt", "new\nline.txt", "trailing-cr\r"]);
        }

        foreach (var path in paths)
        {
            repo.WriteBytes(path, Encoding.UTF8.GetBytes("committed " + path));
        }

        repo.Commit("first");
        foreach (var path in paths)
        {
            repo.WriteBytes(path, Encoding.UTF8.GetBytes(path == "empty.txt" ? "" : $"content of {path}\r\nsecond line\n"));
        }

        var committed = Staged(repo);

        var hashes = await new GitBlobHasher(repo.Root).HashFilesAsync(paths, CancellationToken.None);

        Assert.Equal(committed, Staged(repo));
        repo.Run("add", "-A");
        var staged = Staged(repo);
        Assert.Equal(paths.Count, hashes.Count);
        Assert.All(paths, path => Assert.Equal(staged[path], hashes[path]));
    }

    [Fact]
    public async Task No_paths_returns_nothing_without_running_git()
    {
        var missing = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));

        var hashes = await new GitBlobHasher(missing).HashFilesAsync([], CancellationToken.None);

        Assert.Empty(hashes);
    }

    [Fact]
    public async Task Dirty_crlf_file_under_autocrlf_keeps_its_hash_when_committed_unchanged()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "true");
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("line one\nline two\n"));
        repo.Commit("first");
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("line one\r\nline two\r\nline three\r\n"));

        var dirty = await DirtyHashAsync(repo, "src/a.cs");
        repo.Commit("second");

        Assert.Equal(await CleanHashAsync(repo, "src/a.cs"), dirty);
    }

    [Fact]
    public async Task Dirty_file_committed_with_crlf_keeps_its_hash_under_autocrlf_which_leaves_it_unconverted()
    {
        using var repo = new TempRepo();
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("line one\r\nline two\r\n"));
        repo.Commit("committed with CRLF while autocrlf was off");
        repo.Run("config", "core.autocrlf", "true");
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("line one\r\nline two\r\nline three\r\n"));

        var dirty = await DirtyHashAsync(repo, "src/a.cs");
        repo.Commit("second");

        Assert.Equal(await CleanHashAsync(repo, "src/a.cs"), dirty);
    }

    [Fact]
    public async Task Dirty_file_in_a_linked_worktree_keeps_its_hash_when_committed_unchanged()
    {
        using var repo = new TempRepo();
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("class A {}\n"));
        repo.Commit("first");
        var linked = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));
        repo.Run("worktree", "add", "-q", linked);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(linked, "src", "a.cs"), "class A { int x; }\n");
            var dirty = (await new GitBlobHasher(linked).HashFilesAsync(["src/a.cs"], CancellationToken.None))["src/a.cs"];

            repo.Run("-C", linked, "add", "src/a.cs");

            Assert.Equal(repo.Run("-C", linked, "rev-parse", ":src/a.cs").Trim(), dirty);
            Assert.Equal(repo.Run("rev-parse", "HEAD:src/a.cs").Trim(), repo.Run("rev-parse", ":src/a.cs").Trim());
        }
        finally
        {
            repo.Run("worktree", "remove", "--force", linked);
        }
    }

    [Fact]
    public async Task Dirty_file_in_a_sha256_repository_keeps_its_hash_when_committed_unchanged()
    {
        using var repo = new TempRepo(objectFormat: "sha256");
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("class A {}\n"));
        repo.Commit("first");
        repo.WriteBytes("src/a.cs", Encoding.UTF8.GetBytes("class A { int x; }\n"));

        var dirty = await DirtyHashAsync(repo, "src/a.cs");
        repo.Commit("second");

        Assert.Equal(64, dirty.Length);
        Assert.Equal(await CleanHashAsync(repo, "src/a.cs"), dirty);
    }

    private static Dictionary<string, string> Staged(TempRepo repo) =>
        repo.Run("ls-files", "-s", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(entry => entry[(entry.IndexOf('\t') + 1)..], entry => entry.Split(' ')[1], StringComparer.Ordinal);

    private static async Task<string> DirtyHashAsync(TempRepo repo, string path)
    {
        var file = (await new GitSourceTree(repo.Root).ListFilesAsync(CancellationToken.None)).Single(f => f.Path == path);
        Assert.Null(file.KnownHash);
        IContentHasher hasher = new GitBlobHasher(repo.Root);
        return (await hasher.HashFilesAsync([path], CancellationToken.None))[path];
    }

    private static async Task<string> CleanHashAsync(TempRepo repo, string path)
    {
        var file = (await new GitSourceTree(repo.Root).ListFilesAsync(CancellationToken.None)).Single(f => f.Path == path);
        Assert.Equal(repo.Run("rev-parse", "HEAD:" + path).Trim(), file.KnownHash);
        return file.KnownHash!;
    }
}
