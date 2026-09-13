using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeDependencyAuditor : IDependencyAuditor
{
    public List<ManifestVulnerabilities> Manifests { get; set; } = [];
    public List<string> Diagnostics { get; set; } = [];
    public int Calls { get; private set; }
    public List<string> Reports { get; } = [];

    public static VulnerablePackage Package(string name, Severity severity, string? fixedVersion = "9.9.9") =>
        new(name, "<9.9.9", severity, $"GHSA-{name}", $"https://github.com/advisories/GHSA-{name}", $"{name} is vulnerable", fixedVersion, true);

    public Task<DependencyAudit> AuditAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        Reports.ForEach(message => progress?.Report(message));
        return Task.FromResult(new DependencyAudit(Manifests, Diagnostics));
    }
}
