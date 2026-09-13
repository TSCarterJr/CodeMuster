namespace CodeMuster.Domain;

/// <summary>An isolated agent's response and the Git patch for its assigned file only.</summary>
public sealed record FileFixEdit(string Response, string Patch);

/// <summary>Runs one file's fix in isolation, without changing the coordinator's checkout or ledger.</summary>
public interface IFileFixer
{
    /// <summary>Returns only the assigned file's changes; rejects edits to any other file.</summary>
    Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken);
}
