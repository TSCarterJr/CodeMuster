using System.Security.Cryptography;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Checks the browser artifacts cited by a validated receipt without trusting references alone.</summary>
public static class UxArtifacts
{
    /// <summary>Requires existing PNG, JPEG or WebP content and matching hashes under the repository root.</summary>
    public static async Task ValidateAsync(string evidence, string repoRoot, IFileSystem files, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(evidence);
        var root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var page in document.RootElement.GetProperty("pages").EnumerateArray())
        {
            var relative = page.GetProperty("artifact").GetString()!;
            if (relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Split('/').Any(p => p is "" or "." or ".."))
                throw new JsonException("UX screenshot must have a repo-relative path without traversal");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.Ordinal) || !files.FileExists(path))
                throw new JsonException("UX screenshot is missing: " + relative);
            byte[] bytes;
            try
            {
                bytes = await files.ReadBytesAsync(path, 20 * 1024 * 1024, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new JsonException("UX screenshot cannot be read: " + relative, ex);
            }
            if (!IsImage(bytes)) throw new JsonException("UX artifact is not a supported screenshot: " + relative);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(hash, page.GetProperty("artifact_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new JsonException("UX screenshot hash changed: " + relative);
        }
    }

    private static bool IsImage(byte[] bytes) => bytes.Length >= 24 && (
        bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        || bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
        || bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8));
}
