using System.Text.Json;
using CodeMuster.Domain;
using static CodeMuster.Infrastructure.Audits.AuditOutput;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>Reads the older advisory format that pnpm and yarn still speak: advisories keyed by id, each naming its module and patched range.</summary>
public static class AdvisoryMapJson
{
    /// <summary>Every advisory in a <c>pnpm audit --json</c> or Yarn 3 <c>yarn npm audit --json</c> report. Throws <see cref="InvalidOperationException"/> for an error envelope or any output without <c>advisories</c>.</summary>
    public static IReadOnlyList<VulnerablePackage> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadMap(document.RootElement);
    }

    /// <summary>The advisories of one parsed report object.</summary>
    internal static IReadOnlyList<VulnerablePackage> ReadMap(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            throw Failed(ErrorText(error));
        }

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("advisories", out var advisories) || advisories.ValueKind != JsonValueKind.Object)
        {
            throw NoReport();
        }

        return advisories.EnumerateObject().Select(advisory => Read(advisory.Value)).ToList();
    }

    /// <summary>One advisory object, as both the map format and yarn's line format carry it.</summary>
    internal static VulnerablePackage Read(JsonElement advisory) => new(
        Text(advisory, "module_name") ?? "",
        Text(advisory, "vulnerable_versions") ?? "",
        AuditSeverity.Parse(Text(advisory, "severity")),
        Text(advisory, "github_advisory_id") ?? Identifier(Text(advisory, "url")) ?? Text(advisory, "id") ?? "",
        Text(advisory, "url") ?? "",
        Text(advisory, "title") ?? "",
        Text(advisory, "patched_versions"),
        Direct(advisory));

    private static bool Direct(JsonElement advisory) =>
        !advisory.TryGetProperty("findings", out var findings)
        || findings.ValueKind != JsonValueKind.Array
        || findings.EnumerateArray().Any(finding =>
            !finding.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Array
            || paths.EnumerateArray().Any(path => path.GetString()?.Contains('>') != true));

    private static string? Identifier(string? url) =>
        url is { Length: > 0 } && url.Contains("/advisories/", StringComparison.Ordinal) ? url[(url.LastIndexOf('/') + 1)..] : null;
}
