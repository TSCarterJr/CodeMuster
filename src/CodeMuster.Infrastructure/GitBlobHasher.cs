using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GitBlobHasher(string repoRoot) : IContentHasher
{
    public async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllBytesAsync(Path.Combine(repoRoot, path), cancellationToken).ConfigureAwait(false);
        return Hash(content);
    }

    public static string Hash(ReadOnlySpan<byte> content)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"blob {content.Length}\0"));
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(header);
        sha1.AppendData(content);
        return Convert.ToHexStringLower(sha1.GetHashAndReset());
    }
}
