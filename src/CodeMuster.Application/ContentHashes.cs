using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>The one way current file hashes are computed (D05): a clean file's index blob id, a modified file's recorded hash while its stat is unchanged, and otherwise one batch through the <see cref="IContentHasher"/>.</summary>
internal static class ContentHashes
{
    /// <summary>Current hash of every file in <paramref name="files"/>, keyed by path, reusing <paramref name="recorded"/> hashes through the stat cache.</summary>
    public static async Task<Dictionary<string, string>> CurrentAsync(IContentHasher hasher, IEnumerable<SourceFile> files, IReadOnlyDictionary<string, FileRecord> recorded, CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var modified = new List<string>();
        foreach (var file in files)
        {
            if (file.KnownHash is not null)
            {
                hashes[file.Path] = file.KnownHash;
            }
            else if (recorded.TryGetValue(file.Path, out var previous) && CanReuseHash(file, previous))
            {
                hashes[file.Path] = previous.ContentHash;
            }
            else
            {
                modified.Add(file.Path);
            }
        }

        if (modified.Count > 0)
        {
            foreach (var (path, hash) in await hasher.HashFilesAsync(modified, cancellationToken))
            {
                hashes[path] = hash;
            }
        }

        return hashes;
    }

    private static bool CanReuseHash(SourceFile file, FileRecord previous) =>
        previous.Mtime == file.Mtime
        && previous.Size == file.Size
        && !SameSecond(previous.LastSeen, previous.Mtime);

    private static bool SameSecond(string a, string b) => string.CompareOrdinal(a, 0, b, 0, 19) == 0;
}
