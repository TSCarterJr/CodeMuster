namespace CodeMuster.Infrastructure;

public sealed class GitChangeTracker(string root) : CodeMuster.Domain.IChangeTracker
{
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

    private async Task WriteAsync(string name, string value, CancellationToken cancellationToken)
    {
        var path = await PathAsync(name, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, value, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<string> SnapshotAsync(CancellationToken cancellationToken)
    {
        var index = await GitProcess.RunAsync(root, ["ls-files", "--stage", "-z"], null, cancellationToken);
        var diff = await GitProcess.RunAsync(root, ["diff", "--binary", "--no-ext-diff", "--no-textconv"], null, cancellationToken);
        return CodeMuster.Domain.Hashing.Sha256Hex(index + "\0" + diff);
    }
    public async Task NotifyAsync(CancellationToken cancellationToken) =>
        await WriteAsync("changed", await SnapshotAsync(cancellationToken), cancellationToken);
    public Task AcknowledgeAsync(string snapshot, CancellationToken cancellationToken) => WriteAsync("scanned", snapshot, cancellationToken);
    public async Task<bool> HasChangesAsync(CancellationToken cancellationToken)
    {
        var scanned = await ReadAsync("scanned", cancellationToken);
        return scanned.Length == 0 ? (await ReadAsync("changed", cancellationToken)).Length > 0
            : await SnapshotAsync(cancellationToken) != scanned;
    }
}
