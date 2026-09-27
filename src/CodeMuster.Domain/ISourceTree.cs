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

    /// <summary>The content a file had at <paramref name="commit"/>, or null when the commit does not hold it (D67).</summary>
    Task<string?> ReadFileAtCommitAsync(string commit, string path, CancellationToken cancellationToken);

    /// <summary>Paths that differ between <paramref name="since"/> and HEAD, as git diff --name-only reports them; uncommitted edits are not included.</summary>
    Task<IReadOnlyList<string>> ChangedSinceAsync(string since, CancellationToken cancellationToken);

    /// <summary>True when an ignore file inside the repo (not the user's global excludes or .git/info/exclude) ignores the path.</summary>
    Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken);
}
