namespace CodeMuster.Domain;

/// <summary>The tracked files of one repository (D13). Paths are repo-relative with forward slashes.</summary>
public interface ISourceTree
{
    /// <summary>The HEAD commit SHA.</summary>
    Task<string> HeadCommitAsync(CancellationToken cancellationToken);

    /// <summary>Every tracked file with its stat info and, for clean files, its blob hash.</summary>
    Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken);

    /// <summary>The current working-tree content of a tracked file.</summary>
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken);
}
