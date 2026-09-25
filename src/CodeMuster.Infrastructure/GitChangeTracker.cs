using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

// Tracked files only: scan reviews the index, so an untracked file, nested repository or worktree never affects coverage.
public sealed class GitChangeTracker(string root, Func<string, bool>? include = null) : IChangeTracker
{
    private const string Header = "codemuster-snapshot-1";
    private const int MoveAttempts = 8;

    public async Task<string> SnapshotAsync(CancellationToken cancellationToken)
    {
        var index = await GitProcess.RunAsync(root, ["ls-files", "--stage", "-z"], null, cancellationToken);
        var identities = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in index.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            var fields = record[..tab].Split(' ');
            var path = record[(tab + 1)..];
            if (fields[0] != "160000" && (include?.Invoke(path) ?? true))
            {
                identities[path] = identities.TryGetValue(path, out var stages) ? stages + "," + fields[1] : fields[1];
            }
        }

        var dirty = await GitProcess.RunAsync(root, ["diff", "--name-only", "-z", "--no-renames", "--ignore-submodules", "--no-ext-diff"], null, cancellationToken);
        await IdentifyWorktreeAsync(identities, [.. dirty.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(identities.ContainsKey)], cancellationToken);
        return Header + "\0" + string.Concat(identities.Select(entry => entry.Key + "\0" + entry.Value + "\0"));
    }

    public async Task NotifyAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotAsync(cancellationToken);
        var path = await PathAsync("changed", cancellationToken);
        try
        {
            await WriteAsync(path, snapshot, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException && File.Exists(path))
        {
            // Another hook replaced the file with its own notification, which records the same thing.
        }
    }

    public async Task AcknowledgeAsync(string snapshot, CancellationToken cancellationToken) =>
        await WriteAsync(await PathAsync("scanned", cancellationToken), snapshot, cancellationToken);

    public async Task<ScanChanges> ChangesAsync(CancellationToken cancellationToken)
    {
        if (Parse(await ReadAsync("scanned", cancellationToken)) is not { } scanned)
        {
            // Never scanned, or scanned by a version that stored a single hash, so nothing can be named.
            return new ScanChanges((await ReadAsync("changed", cancellationToken)).Length > 0, []);
        }

        var current = Parse(await SnapshotAsync(cancellationToken))!;
        List<string> changed = [.. current.Keys.Union(scanned.Keys)
            .Where(path => current.GetValueOrDefault(path) != scanned.GetValueOrDefault(path))
            .Order(StringComparer.Ordinal)];
        return new ScanChanges(changed.Count > 0, changed);
    }

    // Identified as git add would store them, symlinks included, so staging or committing unchanged content keeps the identity.
    // A deleted file leaves the snapshot, exactly as staging or committing the deletion would take it out of the index.
    private async Task IdentifyWorktreeAsync(SortedDictionary<string, string> identities, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var present = paths.Where(path => new FileInfo(Path.Combine(root, path)) is var info && (info.LinkTarget is not null || info.Exists)).ToList();
            try
            {
                // The same hasher scan uses (D57): hash-object would miss git's rule that a file committed with CRLF is not converted.
                var hashes = await new GitBlobHasher(root).HashFilesAsync(present, cancellationToken);
                foreach (var path in paths)
                {
                    if (hashes.TryGetValue(path, out var hash))
                    {
                        identities[path] = hash;
                    }
                    else
                    {
                        identities.Remove(path);
                    }
                }

                return;
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
                // A file deleted between the diff and the hash makes git exit 128, so look at the files again.
            }
        }
    }

    private static Dictionary<string, string>? Parse(string snapshot)
    {
        var fields = snapshot.Split('\0');
        if (fields[0] != Header)
        {
            return null;
        }

        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i + 1 < fields.Length; i += 2)
        {
            identities[fields[i]] = fields[i + 1];
        }

        return identities;
    }

    private async Task<string> PathAsync(string name, CancellationToken cancellationToken)
    {
        var path = (await GitProcess.RunAsync(root, ["rev-parse", "--git-path", "codemuster/" + name], null, cancellationToken)).Trim();
        return Path.GetFullPath(path, root);
    }

    private async Task<string> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var path = await PathAsync(name, cancellationToken);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : "";
    }

    private static async Task WriteAsync(string path, string value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, value, cancellationToken);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
                {
                    // Windows refuses to replace a file that another process is replacing or reading at that moment.
                    await Task.Delay(10 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
