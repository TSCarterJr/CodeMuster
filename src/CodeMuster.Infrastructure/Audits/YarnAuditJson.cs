using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>Reads yarn classic's <c>yarn audit --json</c>, which writes one JSON object per line and carries each advisory in an <c>auditAdvisory</c> line.</summary>
public static class YarnAuditJson
{
    /// <summary>Every advisory in the stream; lines that are not advisories, including the summary, are ignored.</summary>
    public static IReadOnlyList<VulnerablePackage> Parse(string output)
    {
        var found = new List<VulnerablePackage>();
        foreach (var line in output.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith('{')))
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == "auditAdvisory"
                && document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("advisory", out var advisory))
            {
                found.Add(AdvisoryMapJson.Read(advisory));
            }
        }

        return found;
    }
}
