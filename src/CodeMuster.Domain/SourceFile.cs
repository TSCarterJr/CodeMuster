namespace CodeMuster.Domain;

/// <summary>One tracked file as the tree reports it.</summary>
/// <param name="Path">Repo-relative path with forward slashes.</param>
/// <param name="Size">Size in bytes on disk.</param>
/// <param name="Mtime">Modified time on disk, formatted by <see cref="Timestamps"/>.</param>
/// <param name="KnownHash">Git blob SHA when the file is clean, or null when it is modified and must be hashed.</param>
/// <param name="LastCommit">SHA of the last commit touching the path, when known.</param>
/// <param name="LastCommitAt">Committer date of that commit, when known.</param>
/// <param name="LinguistGenerated">True when .gitattributes marks the path linguist-generated.</param>
public sealed record SourceFile(string Path, long Size, string Mtime, string? KnownHash, string? LastCommit, string? LastCommitAt, bool LinguistGenerated);
