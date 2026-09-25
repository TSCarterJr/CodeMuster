namespace CodeMuster.Domain;

/// <summary>Lightweight edit notifications stored separately from the ledger and working tree.</summary>
public interface IChangeTracker
{
    /// <summary>The tracked files a scan would review and the content identity of each, as text <see cref="AcknowledgeAsync"/> can store.</summary>
    Task<string> SnapshotAsync(CancellationToken cancellationToken);
    /// <summary>Records that an agent tool may have changed repository content.</summary>
    Task NotifyAsync(CancellationToken cancellationToken);
    /// <summary>Acknowledges only the snapshot taken before a completed scan.</summary>
    Task AcknowledgeAsync(string snapshot, CancellationToken cancellationToken);
    /// <summary>What changed since the acknowledged snapshot; before any acknowledged snapshot, whether a notification has arrived.</summary>
    Task<ScanChanges> ChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Reviewed files that changed since the last scan.</summary>
/// <param name="Detected">Whether anything changed.</param>
/// <param name="Paths">Repo-relative paths whose content changed, in ordinal order; empty when a notification arrived but no acknowledged snapshot exists to name the files.</param>
public sealed record ScanChanges(bool Detected, IReadOnlyList<string> Paths);
