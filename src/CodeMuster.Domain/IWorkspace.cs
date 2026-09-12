namespace CodeMuster.Domain;

/// <summary>The repository as fix mode changes it (D37): fix is the only verb that writes, so it checks and commits through this.</summary>
public interface IWorkspace
{
    /// <summary>True when the working tree has no uncommitted changes to tracked files.</summary>
    Task<bool> IsCleanAsync(CancellationToken cancellationToken);

    /// <summary>Commits everything currently changed, with <paramref name="message"/>. Never pushes.</summary>
    Task CommitAsync(string message, CancellationToken cancellationToken);

    /// <summary>Throws away uncommitted changes to tracked files, so a failed attempt does not leave half a fix behind.</summary>
    Task RestoreAsync(CancellationToken cancellationToken);
}
