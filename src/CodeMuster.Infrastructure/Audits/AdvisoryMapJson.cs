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

        var direct = DirectIds(root);
        return advisories.EnumerateObject().Select(advisory => Read(advisory.Value, direct)).ToList();
    }

    /// <summary>One advisory object, as both the map format and yarn's line format carry it.</summary>
    internal static VulnerablePackage Read(JsonElement advisory, IReadOnlySet<string>? directIds = null) => new(
        Text(advisory, "module_name") ?? "",
        Text(advisory, "vulnerable_versions") ?? "",
        AuditSeverity.Parse(Text(advisory, "severity")),
        Text(advisory, "github_advisory_id") ?? Identifier(Text(advisory, "url")) ?? Text(advisory, "id") ?? "",
        Text(advisory, "url") ?? "",
        Text(advisory, "title") ?? "",
        Text(advisory, "patched_versions"),
        Direct(advisory, directIds ?? new HashSet<string>()));

    private static bool Direct(JsonElement advisory, IReadOnlySet<string> directIds)
    {
        if (!advisory.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var listed = false;
        foreach (var finding in findings.EnumerateArray())
        {
            if (!finding.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            foreach (var path in paths.EnumerateArray())
            {
                listed = true;
                if (path.GetString()?.Contains('>') != true)
                {
                    return true;
                }
            }
        }

        // pnpm leaves every findings path empty and lists the paths under actions[].resolves[] instead.
        return !listed && Text(advisory, "id") is { } id && directIds.Contains(id);
    }

    /// <summary>Ids of the advisories an <c>actions[].resolves[]</c> entry reaches in one hop, as pnpm writes <c>.&gt;minimist</c> for a package the manifest declares.</summary>
    private static HashSet<string> DirectIds(JsonElement root) =>
        root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array
            ? actions.EnumerateArray()
                .Where(action => action.ValueKind == JsonValueKind.Object && action.TryGetProperty("resolves", out var resolves) && resolves.ValueKind == JsonValueKind.Array)
                .SelectMany(action => action.GetProperty("resolves").EnumerateArray())
                .Where(resolve => Text(resolve, "path") is { } path && path.Split('>').Length <= 2)
                .Select(resolve => Text(resolve, "id"))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static string? Identifier(string? url) =>
        url is { Length: > 0 } && url.Contains("/advisories/", StringComparison.Ordinal) ? url[(url.LastIndexOf('/') + 1)..] : null;
}
