namespace CodeMuster.Domain;

/// <summary>Hashes modified working-tree files to the object id git would record if they were staged now, the same id a clean file carries, so the ledger holds one hash scheme and committing content unchanged keeps its hash (D05).</summary>
public interface IContentHasher
{
    /// <summary>The object id of each repo-relative path's current content after the repository's clean filters, line-ending rules and object format, all paths in one pass.</summary>
    Task<IReadOnlyDictionary<string, string>> HashFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);
}
