using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class GitBlobHasherTests
{
    [Fact]
    public void Hash_matches_git_hash_object_for_known_string()
    {
        using var repo = new TempRepo();
        var bytes = Encoding.UTF8.GetBytes("hello world\n");
        repo.WriteBytes("hello.txt", bytes);
        var expected = repo.Run("hash-object", "hello.txt").Trim();

        Assert.Equal(expected, GitBlobHasher.Hash(bytes));
    }

    [Fact]
    public void Hash_of_empty_content_matches_git()
    {
        using var repo = new TempRepo();
        repo.WriteBytes("empty.txt", []);
        var expected = repo.Run("hash-object", "empty.txt").Trim();

        Assert.Equal(expected, GitBlobHasher.Hash(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task HashFileAsync_matches_git_for_crlf_bytes_without_normalizing()
    {
        using var repo = new TempRepo();
        var crlf = Encoding.UTF8.GetBytes("line one\r\nline two\r\n");
        repo.WriteBytes("src/a/crlf.cs", crlf);
        var expected = repo.Run("hash-object", "src/a/crlf.cs").Trim();
        IContentHasher hasher = new GitBlobHasher(repo.Root);

        var actual = await hasher.HashFileAsync("src/a/crlf.cs", CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.NotEqual(GitBlobHasher.Hash(Encoding.UTF8.GetBytes("line one\nline two\n")), actual);
    }

    [Fact]
    public void Hash_is_forty_lowercase_hex_characters()
    {
        var hash = GitBlobHasher.Hash(Encoding.UTF8.GetBytes("x"));

        Assert.Equal(40, hash.Length);
        Assert.All(hash, c => Assert.True(char.IsAsciiHexDigitLower(c)));
    }
}
