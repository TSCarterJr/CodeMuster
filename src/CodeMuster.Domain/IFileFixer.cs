namespace CodeMuster.Domain;

/// <summary>An isolated agent's response and the Git patch for its assigned file only, with the worker copy the patch came from while it is still on disk.</summary>
public sealed record FileFixEdit(string Response, string Patch, string? Worktree = null);

/// <summary>Runs one file's fix in isolation, without changing the coordinator's checkout or ledger.</summary>
public interface IFileFixer
{
    /// <summary>Returns only the assigned file's changes; rejects edits to any other file. The worker copy stays on disk until the edit is released.</summary>
    Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken);

    /// <summary>Discards the worker copy once the coordinator has used or rejected its patch; an edit that is never released stays on disk so its finished work can be recovered.</summary>
    Task ReleaseAsync(FileFixEdit edit, CancellationToken cancellationToken) => Task.CompletedTask;
}
