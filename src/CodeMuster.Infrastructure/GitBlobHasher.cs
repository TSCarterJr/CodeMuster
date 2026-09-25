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
        var copy = Path.Combine(Path.GetTempPath(), "codemuster-index-" + Guid.NewGuid().ToString("N"));
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
                File.Delete(copy);
            }
            catch (IOException)
            {
                // A scanner holding the copy open on Windows must not fail the scan; a leftover temp file is harmless.
            }
        }

        return hashes;
    }
}
