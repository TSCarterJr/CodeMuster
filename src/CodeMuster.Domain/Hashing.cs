using System.Security.Cryptography;
using System.Text;

namespace CodeMuster.Domain;

/// <summary>The one content hash used for fingerprints and lens hashes.</summary>
public static class Hashing
{
    /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
