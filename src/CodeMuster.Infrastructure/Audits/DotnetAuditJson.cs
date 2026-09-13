using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>Reads <c>dotnet list package --vulnerable --include-transitive --format json</c>. NuGet's audit names the advisory and its severity but never the version that fixes it.</summary>
public static class DotnetAuditJson
{
    /// <summary>Every advisory, grouped under the project that resolves the package.</summary>
    public static IReadOnlyList<(string Project, VulnerablePackage Package)> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<(string, VulnerablePackage)>();
        foreach (var project in projects.EnumerateArray())
        {
            var path = project.TryGetProperty("path", out var value) ? value.GetString() ?? "" : "";
            if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                Collect(framework, "topLevelPackages", direct: true, path, found);
                Collect(framework, "transitivePackages", direct: false, path, found);
            }
        }

        return found;
    }

    private static void Collect(JsonElement framework, string property, bool direct, string project, List<(string, VulnerablePackage)> found)
    {
        if (!framework.TryGetProperty(property, out var packages) || packages.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var package in packages.EnumerateArray())
        {
            var id = package.TryGetProperty("id", out var name) ? name.GetString() ?? "" : "";
            var resolved = package.TryGetProperty("resolvedVersion", out var version) ? version.GetString() ?? "" : "";
            if (!package.TryGetProperty("vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var vulnerability in vulnerabilities.EnumerateArray())
            {
                var url = vulnerability.TryGetProperty("advisoryurl", out var advisory) ? advisory.GetString() ?? "" : "";
                var severity = vulnerability.TryGetProperty("severity", out var level) ? level.GetString() : null;
                found.Add((project, new VulnerablePackage(
                    id,
                    resolved,
                    AuditSeverity.Parse(severity),
                    url.Contains("/advisories/", StringComparison.Ordinal) ? url[(url.LastIndexOf('/') + 1)..] : url,
                    url,
                    $"{id} {resolved} has a known {severity?.ToLowerInvariant()} severity vulnerability",
                    null,
                    direct)));
            }
        }
    }
}
