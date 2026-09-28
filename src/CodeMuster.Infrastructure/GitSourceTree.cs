using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class NotARepositoryException(string message) : InvalidOperationException(message);

public sealed class GitSourceTree(string repoRoot) : ISourceTree
{
    public static async Task<string> FindTopLevelAsync(string directory, CancellationToken cancellationToken)
    {
        string[] arguments = ["rev-parse", "--show-toplevel"];
        // English messages, so "not a git repository" can be told apart from other failures such as dubious ownership.
        var (exitCode, output, error) = await GitProcess.RunAllowingFailureAsync(directory, arguments, null, cancellationToken, new Dictionary<string, string> { ["LC_ALL"] = "C" }).ConfigureAwait(false);
        return exitCode == 0 ? Path.GetFullPath(output.Trim())
            : error.Contains("not a git repository", StringComparison.Ordinal)
                ? throw new NotARepositoryException($"{directory} is not a git repository or inside one; run codemuster from a repository, or create one with git init")
                : throw GitProcess.Failure(arguments, exitCode, error);
    }

    public async Task<string> HeadCommitAsync(CancellationToken cancellationToken)
    {
        var output = await GitProcess.RunAsync(repoRoot, ["rev-parse", "HEAD"], null, cancellationToken).ConfigureAwait(false);
        return output.Trim();
    }

    public async Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var index = ParseIndex(await GitProcess.RunAsync(repoRoot, ["ls-files", "-s", "-z"], null, cancellationToken).ConfigureAwait(false));
        var dirty = ParseDirtyPaths(await GitProcess.RunAsync(repoRoot, ["status", "--porcelain", "-z", "--untracked-files=no"], null, cancellationToken).ConfigureAwait(false));
        var pathList = string.Concat(index.Select(entry => entry.Path + "\0"));
        var generated = ParseGenerated(await GitProcess.RunAsync(repoRoot, ["check-attr", "linguist-generated", "-z", "--stdin"], pathList, cancellationToken).ConfigureAwait(false));

        var files = new List<SourceFile>(index.Count);
        foreach (var (path, sha) in index)
        {
            var info = new FileInfo(Path.Combine(repoRoot, path));
            if (!info.Exists)
            {
                continue;
            }

            files.Add(new SourceFile(
                path,
                info.Length,
                Timestamps.Format(new DateTimeOffset(info.LastWriteTimeUtc)),
                dirty.Contains(path) ? null : sha,
                generated.Contains(path)));
        }

        return files;
    }

    // Without rename detection a name-only walk compares trees only, so a blobless clone fetches no blobs. The walk is not cut short
    // once every path is found: some file usually dates from near the root (at 25,092 commits the last was found 27 from the end).
    public async Task<IReadOnlyDictionary<string, CommitStamp>> LastCommitsAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        var output = await GitProcess.RunAsync(repoRoot, ["log", "--no-renames", "-z", "--name-only", "--format=%x01%H%x00%cI"], null, cancellationToken).ConfigureAwait(false);
        return ParseLastCommits(output, paths.ToHashSet(StringComparer.Ordinal));
    }

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(Path.Combine(repoRoot, path), cancellationToken);

    public async Task<string?> ReadFileAtCommitAsync(string commit, string path, CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await GitProcess.RunAllowingFailureAsync(repoRoot, ["show", commit + ":" + path], null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? output : null;
    }

    public async Task<IReadOnlyList<string>> ChangedSinceAsync(string since, CancellationToken cancellationToken)
    {
        var output = await GitProcess.RunAsync(repoRoot, ["-c", "core.quotePath=false", "diff", "--name-only", "-z", since, "HEAD", "--"], null, cancellationToken).ConfigureAwait(false);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(RepoPath.Normalize).ToList();
    }

    public async Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken)
    {
        string[] arguments = ["check-ignore", "-v", "-z", "--stdin"];
        var (exitCode, output, error) = await GitProcess.RunAllowingFailureAsync(repoRoot, arguments, path + "\0", cancellationToken).ConfigureAwait(false);
        if (exitCode == 1)
        {
            return false;
        }

        if (exitCode != 0)
        {
            throw GitProcess.Failure(arguments, exitCode, error);
        }

        var fields = output.Split('\0');
        var source = RepoPath.Normalize(fields[0]);
        var pattern = fields[2];
        return !pattern.StartsWith('!') && !Path.IsPathRooted(source) && !source.StartsWith(".git/", StringComparison.Ordinal);
    }

    internal static List<(string Path, string Sha)> ParseIndex(string output)
    {
        var entries = new List<(string Path, string Sha)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            var fields = record[..tab].Split(' ');
            var path = RepoPath.Normalize(record[(tab + 1)..]);
            if (seen.Add(path))
            {
                entries.Add((path, fields[1]));
            }
        }

        return entries;
    }

    private static HashSet<string> ParseDirtyPaths(string output)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var entry = tokens[i];
            if (entry[0] is 'R' or 'C')
            {
                i++;
            }

            if (entry[1] != ' ')
            {
                dirty.Add(RepoPath.Normalize(entry[3..]));
            }
        }

        return dirty;
    }

    // With -z each commit is "\x01<sha>", "<date>", then its paths, the first one after a newline, all NUL-terminated; a merge lists none.
    private static Dictionary<string, CommitStamp> ParseLastCommits(string output, HashSet<string> wanted)
    {
        var result = new Dictionary<string, CommitStamp>(StringComparer.Ordinal);
        CommitStamp? commit = null;
        var tokens = output.Split('\0');
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].StartsWith('\u0001'))
            {
                commit = new CommitStamp(tokens[i][1..], tokens[++i]);
                continue;
            }

            var path = RepoPath.Normalize(tokens[i].TrimStart('\n'));
            if (commit is not null && path.Length > 0 && wanted.Contains(path))
            {
                result.TryAdd(path, commit);
            }
        }

        return result;
    }

    internal static HashSet<string> ParseGenerated(string output)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);
        var tokens = output.Split('\0');
        for (var i = 0; i + 2 < tokens.Length; i += 3)
        {
            if (tokens[i + 2] is "set" or "true")
            {
                generated.Add(RepoPath.Normalize(tokens[i]));
            }
        }

        return generated;
    }
}
