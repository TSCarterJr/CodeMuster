namespace CodeMuster.Domain;

/// <summary>A batch mapper for one language (D08, D26).</summary>
public interface ICodeMapper
{
    /// <summary>The language this mapper covers, one of the <see cref="Languages"/> constants.</summary>
    string Language { get; }

    /// <summary>Maps the repository at <paramref name="repoRoot"/>. <paramref name="paths"/> are the repo's included files, repo-relative, used to find solutions, projects, and tsconfig files without walking ignored folders. Returns a partial map with diagnostics when some references cannot be resolved, and throws when the language cannot be mapped at all, with a message that says why. <paramref name="progress"/>, when given, receives short steps as they happen, such as a project loading or a count of mapped files, without the language name.</summary>
    Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken);
}
