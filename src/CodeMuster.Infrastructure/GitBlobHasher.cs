using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GitBlobHasher(string repoRoot) : IContentHasher
{
    public async Task<IReadOnlyDictionary<string, string>> HashFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (paths.Count == 0)
        {
            return hashes;
        }

        var input = string.Concat(paths.Select(path => Quote(path) + "\n"));
        var output = await GitProcess.RunAsync(repoRoot, ["hash-object", "--stdin-paths"], input, cancellationToken).ConfigureAwait(false);
        var ids = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length != paths.Count)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"git hash-object returned {ids.Length} ids for {paths.Count} paths"));
        }

        for (var i = 0; i < paths.Count; i++)
        {
            hashes[paths[i]] = ids[i].TrimEnd('\r');
        }

        return hashes;
    }

    // --stdin-paths reads one path per line and C-unquotes a line that starts with a double quote, so quoting every path carries any name, even one with a newline.
    private static string Quote(string path) =>
        "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
}
