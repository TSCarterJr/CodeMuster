namespace CodeMuster.Domain;

/// <summary>The repository as fix mode changes it (D37): fix is the only verb that writes, so it checks and commits through this.</summary>
public interface IWorkspace
{
    /// <summary>True when the working tree has no uncommitted changes to tracked files.</summary>
    Task<bool> IsCleanAsync(CancellationToken cancellationToken);

    /// <summary>Commits changes to tracked files with <paramref name="message"/>, leaving untracked files alone. Never pushes.</summary>
    Task CommitAsync(string message, CancellationToken cancellationToken);

    /// <summary>Throws away uncommitted changes to tracked files, so a failed attempt does not leave half a fix behind.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Saves tracked changes and their index state, returning the exact stash commit. Leaves untracked files alone.</summary>
    Task<string> StashAsync(CancellationToken cancellationToken);

    /// <summary>Restores the saved tracked changes and index, retaining the stash as a recovery copy. Saves unfinished fix edits separately first.</summary>
    Task RestoreStashAsync(string stash, CancellationToken cancellationToken);

    /// <summary>Applies a validated isolated worker patch to the working tree.</summary>
    Task ApplyPatchAsync(string patch, CancellationToken cancellationToken);

    /// <summary>Checks for tracked edits to one repo-relative file.</summary>
    Task<bool> HasFileChangesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Commits only the assigned file, leaving other paths and their staging state alone.</summary>
    Task CommitFileAsync(string path, string message, CancellationToken cancellationToken);

    /// <summary>Restores only the assigned file and its index entry to HEAD.</summary>
    Task RestoreFileAsync(string path, CancellationToken cancellationToken);
}
