namespace CodeMuster.Domain;

/// <summary>A batch mapper for one language (D08).</summary>
public interface ICodeMapper
{
    /// <summary>Maps the repository at <paramref name="repoRoot"/>.</summary>
    Task<CodeMap> MapAsync(string repoRoot, CancellationToken cancellationToken);
}
