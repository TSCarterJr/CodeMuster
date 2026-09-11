namespace CodeMuster.Domain;

/// <summary>Hashes working-tree content with the same scheme git uses for blobs, so the ledger holds one hash scheme (D05).</summary>
public interface IContentHasher
{
    /// <summary>Git blob SHA of the current bytes of a repo-relative path.</summary>
    Task<string> HashFileAsync(string path, CancellationToken cancellationToken);
}
