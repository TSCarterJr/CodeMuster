using System.Text.Json;

namespace CodeMuster.Infrastructure;

// Harness output formats change between releases, so usage is read leniently: a field that is missing or of another type counts as unreported and never fails a call.
internal static class UsageJson
{
    public static JsonElement? Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    public static long? Long(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    public static decimal? Decimal(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;

    public static string? String(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static long? Sum(long? left, long? right) => left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    public static JsonDocument? TryParse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // One JSON object per line; lines that are not JSON, such as warnings, are skipped.
    public static IEnumerable<JsonDocument> Lines(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('{') && TryParse(trimmed) is { } document) yield return document;
        }
    }
}
