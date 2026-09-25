using System.Text.Json;
using CodeMuster.Domain;
using static CodeMuster.Infrastructure.Audits.AuditOutput;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>
/// Reads yarn's audit output, one JSON object per line: yarn classic's <c>yarn audit --json</c> carries each advisory in an <c>auditAdvisory</c> line and ends with an <c>auditSummary</c>,
/// Yarn 4's <c>yarn npm audit --json</c> writes one <c>value</c>/<c>children</c> line per advisory, and Yarn 3's writes a single advisory map.
/// </summary>
public static class YarnAuditJson
{
    /// <summary>Every advisory in the stream. Throws <see cref="InvalidOperationException"/> for an <c>error</c> line or when no line is part of a report, as when yarn classic fails offline and writes only an <c>info</c> line to stdout.</summary>
    public static IReadOnlyList<VulnerablePackage> Parse(string output)
    {
        var found = new List<VulnerablePackage>();
        var report = false;
        foreach (var line in output.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith('{')))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = Text(root, "type");
            if (type == "error")
            {
                throw Failed(Text(root, "data"));
            }

            if (root.TryGetProperty("value", out var name)
                && root.TryGetProperty("children", out var details)
                && details.TryGetProperty("URL", out var url))
            {
                var link = url.GetString() ?? "";
                found.Add(new VulnerablePackage(name.GetString() ?? "", details.GetProperty("Vulnerable Versions").GetString() ?? "",
                    AuditSeverity.Parse(details.GetProperty("Severity").GetString()), link[(link.LastIndexOf('/') + 1)..], link,
                    details.GetProperty("Issue").GetString() ?? "", null,
                    details.TryGetProperty("Dependents", out var dependents) && dependents.EnumerateArray().Any(d => d.GetString()?.Contains("@workspace:", StringComparison.Ordinal) == true)));
                report = true;
            }
            else if (root.TryGetProperty("advisories", out _))
            {
                found.AddRange(AdvisoryMapJson.ReadMap(root));
                report = true;
            }
            else if (type == "auditAdvisory"
                && root.TryGetProperty("data", out var data)
                && data.TryGetProperty("advisory", out var advisory))
            {
                found.Add(AdvisoryMapJson.Read(advisory));
                report = true;
            }
            else if (type == "auditSummary")
            {
                report = true;
            }
        }

        return report ? found : throw NoReport();
    }
}
