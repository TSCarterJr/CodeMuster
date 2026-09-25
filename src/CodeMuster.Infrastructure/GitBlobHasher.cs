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

        // Staging into a copy of the index gives the id `git add` would record, including git's rule that a file committed with CRLF
        // is not converted, which hash-object cannot see. --info-only writes no object, and an unsplit copy writes nothing into .git.
        var index = Path.Combine(repoRoot, (await GitProcess.RunAsync(repoRoot, ["rev-parse", "--git-path", "index"], null, cancellationToken).ConfigureAwait(false)).Trim());
        // A folder of its own is created with mode 0700 on Linux and macOS, so other users of a shared /tmp cannot read the copy's
        // paths, blob ids and stat data; the copy inherits the index's 0644 and git rewrites it with 0666 less the umask.
        var folder = Directory.CreateTempSubdirectory("codemuster-index-");
        var copy = Path.Combine(folder.FullName, "index");
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = copy };
        try
        {
            if (File.Exists(index))
            {
                File.Copy(index, copy);
            }

            await GitProcess.RunAsync(repoRoot, ["-c", "core.splitIndex=false", "update-index", "--add", "--info-only", "-z", "--stdin"], string.Concat(paths.Select(path => path + "\0")), cancellationToken, environment).ConfigureAwait(false);
            var staged = GitSourceTree.ParseIndex(await GitProcess.RunAsync(repoRoot, ["ls-files", "-s", "-z"], null, cancellationToken, environment).ConfigureAwait(false))
                .ToDictionary(entry => entry.Path, entry => entry.Sha, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                hashes[path] = staged.TryGetValue(RepoPath.Normalize(path), out var sha) ? sha : throw new InvalidOperationException($"git update-index recorded no id for {path}");
            }
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A scanner holding the copy open on Windows must not fail the scan; a leftover private temp folder is harmless.
            }
        }

        return hashes;
    }
}
