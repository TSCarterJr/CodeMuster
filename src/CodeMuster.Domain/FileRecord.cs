namespace CodeMuster.Domain;

/// <summary>One tracked file as the ledger last saw it. Paths are repo-relative with forward slashes; timestamps are UTC ISO 8601.</summary>
/// <param name="Path">Repo-relative path.</param>
/// <param name="Language">A language code such as csharp or typescript.</param>
/// <param name="ContentHash">Git blob SHA of the content when last hashed (D05).</param>
/// <param name="Size">Size in bytes when last hashed.</param>
/// <param name="Mtime">Modified time observed when last hashed; a stat-cache key only.</param>
/// <param name="FirstSeen">When the ledger first recorded the path.</param>
/// <param name="LastSeen">When the ledger last saw the path in the tree.</param>
/// <param name="LastCommit">SHA of the last commit touching the path, when known.</param>
/// <param name="LastCommitAt">Committer date of that commit, when known.</param>
/// <param name="ExcludedReason">Why the file is not analyzed, or null when it is.</param>
/// <param name="DeletedAt">When the file vanished from the tree, or null while present.</param>
/// <param name="Summary">One-line summary from the last analysis (D12).</param>
/// <param name="SummaryHash">Content hash the summary was generated from.</param>
public sealed record FileRecord(
    string Path,
    string Language,
    string ContentHash,
    long Size,
    string Mtime,
    string FirstSeen,
    string LastSeen,
    string? LastCommit,
    string? LastCommitAt,
    string? ExcludedReason,
    string? DeletedAt,
    string? Summary,
    string? SummaryHash);
