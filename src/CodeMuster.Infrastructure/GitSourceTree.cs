using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class GitSourceTree(string repoRoot) : ISourceTree
{
    public static async Task<string> FindTopLevelAsync(string directory, CancellationToken cancellationToken)
    {
        var output = await GitProcess.RunAsync(directory, ["rev-parse", "--show-toplevel"], null, cancellationToken).ConfigureAwait(false);
        return Path.GetFullPath(output.Trim());
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
        var lastCommits = ParseLastCommits(await GitProcess.RunAsync(repoRoot, ["-c", "core.quotePath=false", "log", "--name-only", "--format=%x01%H%x00%cI"], null, cancellationToken).ConfigureAwait(false));
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

            var hasCommit = lastCommits.TryGetValue(path, out var commit);
            files.Add(new SourceFile(
                path,
                info.Length,
                Timestamps.Format(new DateTimeOffset(info.LastWriteTimeUtc)),
                dirty.Contains(path) ? null : sha,
                hasCommit ? commit.Sha : null,
                hasCommit ? commit.Date : null,
                generated.Contains(path)));
        }

        return files;
    }

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(Path.Combine(repoRoot, path), cancellationToken);

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

    private static Dictionary<string, (string Sha, string Date)> ParseLastCommits(string output)
    {
        var result = new Dictionary<string, (string Sha, string Date)>(StringComparer.Ordinal);
        foreach (var chunk in output.Split('', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = chunk.Split('\n');
            var header = lines[0].TrimEnd('\r').Split('\0');
            var commit = (Sha: header[0], Date: header[1]);
            foreach (var line in lines.Skip(1))
            {
                var path = line.TrimEnd('\r');
                if (path.Length > 0)
                {
                    result.TryAdd(RepoPath.Normalize(path), commit);
                }
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
