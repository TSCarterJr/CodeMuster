using System.Globalization;

namespace CodeMuster.Domain;

/// <summary>The one timestamp format stored anywhere: UTC ISO 8601 with seven fractional digits.</summary>
public static class Timestamps
{
    /// <summary>Formats as UTC, e.g. 2026-09-10T23:20:43.6570000Z.</summary>
    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
