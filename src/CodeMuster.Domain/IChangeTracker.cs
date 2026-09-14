namespace CodeMuster.Domain;

/// <summary>Lightweight edit notifications stored separately from the ledger and working tree.</summary>
public interface IChangeTracker
{
    /// <summary>The latest notification token, or an empty string before any notification.</summary>
    Task<string> SnapshotAsync(CancellationToken cancellationToken);
    /// <summary>Records that an agent tool may have changed repository content.</summary>
    Task NotifyAsync(CancellationToken cancellationToken);
    /// <summary>Acknowledges only the snapshot taken before a completed scan.</summary>
    Task AcknowledgeAsync(string snapshot, CancellationToken cancellationToken);
    /// <summary>Whether notifications have arrived since the acknowledged snapshot.</summary>
    Task<bool> HasChangesAsync(CancellationToken cancellationToken);
}
