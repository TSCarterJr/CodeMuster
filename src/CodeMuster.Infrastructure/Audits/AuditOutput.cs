using System.Text.Json;

namespace CodeMuster.Infrastructure.Audits;

/// <summary>
/// Shared reading helpers for the audit parsers. A tool that fails offline or cannot restore still writes valid JSON,
/// so each parser must reject output that is not a report; reading it as an empty report would erase earlier findings (D38).
/// </summary>
internal static class AuditOutput
{
    public static InvalidOperationException Failed(params string?[] reasons) =>
        new((FirstText(reasons) ?? "it reported an error without a message").Split('\n')[0].Trim());

    public static InvalidOperationException NoReport() => new("no audit report in its output");

    /// <summary>The human-readable part of an <c>error</c> value, which tools write as a string or as an object.</summary>
    public static string? ErrorText(JsonElement error) => error.ValueKind == JsonValueKind.String
        ? error.GetString()
        : FirstText(Text(error, "message"), Text(error, "summary"), Text(error, "code"));

    private static string? FirstText(params string?[] texts) => texts.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            }
            : null;
}
