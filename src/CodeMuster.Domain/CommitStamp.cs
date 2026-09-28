namespace CodeMuster.Domain;

/// <summary>The last commit that touched a path.</summary>
/// <param name="Sha">The commit SHA.</param>
/// <param name="At">Its committer date, ISO 8601 as git prints it.</param>
public sealed record CommitStamp(string Sha, string At);
