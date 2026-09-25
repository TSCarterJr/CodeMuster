namespace CodeMuster.Domain;

/// <summary>What one manifest's audit found (D38).</summary>
/// <param name="Manifest">Repo-relative path of the manifest the packages belong to.</param>
/// <param name="Tool">The command that produced it, for the record and the report.</param>
/// <param name="Packages">Every advisory the tool reported.</param>
public sealed record ManifestVulnerabilities(string Manifest, string Tool, IReadOnlyList<VulnerablePackage> Packages);

/// <summary>Every manifest audited in one pass, and whatever went wrong (D38).</summary>
/// <param name="Manifests">One entry per manifest whose audit ran and could be read.</param>
/// <param name="Diagnostics">Manifests whose tool is missing, failed, or wrote something unreadable, each naming the fix, and manifests whose folder holds lockfiles for more than one tool, naming the tool used.</param>
public sealed record DependencyAudit(IReadOnlyList<ManifestVulnerabilities> Manifests, IReadOnlyList<string> Diagnostics);

/// <summary>Runs each ecosystem's own audit tool over the repository's manifests (D38). Never installs or restores anything.</summary>
public interface IDependencyAuditor
{
    /// <summary>Audits every manifest among <paramref name="paths"/>, reporting each one to <paramref name="progress"/> as it starts.</summary>
    Task<DependencyAudit> AuditAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken);
}
