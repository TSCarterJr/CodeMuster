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

    public static InvalidOperationException NoReport() => new(NoReportMessage);

    public const string NoReportMessage = "no audit report in its output";

    /// <summary>The first line of the tool's own complaint on stderr: the data of a yarn <c>{"type":"error"}</c> line, else the first non-blank line.</summary>
    public static string? StderrReason(string error)
    {
        var lines = error.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        foreach (var line in lines.Where(line => line.StartsWith('{')))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (Text(document.RootElement, "type") == "error" && Text(document.RootElement, "data") is { } data)
                {
                    return FirstLine(data);
                }
            }
            catch (JsonException)
            {
                // Not one of yarn's JSON lines; the first plain line below stands in for it.
            }
        }

        return lines.Count == 0 ? null : lines[0];
    }

    /// <summary>The first non-blank line of <paramref name="text"/>, trimmed, or null when there is none.</summary>
    public static string? FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);

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
