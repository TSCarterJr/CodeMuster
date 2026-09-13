using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>Reads <c>npm audit --json</c> as npm 7 and later write it: a map of package name to what is wrong with it.</summary>
public static class NpmAuditJson
{
    /// <summary>Every advisory the report names, ordered by package then advisory.</summary>
    public static IReadOnlyList<VulnerablePackage> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("vulnerabilities", out var packages) || packages.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var found = new List<VulnerablePackage>();
        foreach (var package in packages.EnumerateObject())
        {
            var value = package.Value;
            var range = Text(value, "range") ?? "";
            var direct = value.TryGetProperty("isDirect", out var isDirect) && isDirect.ValueKind == JsonValueKind.True;
            var fixedVersion = FixedVersion(value);
            foreach (var via in Advisories(value))
            {
                found.Add(new VulnerablePackage(
                    package.Name,
                    range,
                    AuditSeverity.Parse(Text(via, "severity") ?? Text(value, "severity")),
                    AdvisoryId(Text(via, "url"), Text(via, "source")),
                    Text(via, "url") ?? "",
                    Text(via, "title") ?? "",
                    fixedVersion,
                    direct));
            }
        }

        return found;
    }

    private static IEnumerable<JsonElement> Advisories(JsonElement package)
    {
        if (!package.TryGetProperty("via", out var via) || via.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in via.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object))
        {
            yield return entry;
        }
    }

    private static string? FixedVersion(JsonElement package) =>
        package.TryGetProperty("fixAvailable", out var fix) && fix.ValueKind == JsonValueKind.Object
            ? Text(fix, "version")
            : null;

    private static string AdvisoryId(string? url, string? source) =>
        url is { Length: > 0 } && url.Contains("/advisories/", StringComparison.Ordinal)
            ? url[(url.LastIndexOf('/') + 1)..]
            : source ?? "";

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            }
            : null;
}
