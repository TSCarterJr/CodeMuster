using System.Text.Json.Serialization;

namespace CodeMuster.Application;

/// <summary>One model's API price in US dollars per million tokens (D63). A longer model id that only adds a release date or a bracketed context tag, such as <c>claude-haiku-4-5-20251001</c>, matches <see cref="Model"/> too. A missing cache rate is charged at the input rate.</summary>
/// <param name="Model">The model id, compared case-insensitively.</param>
/// <param name="Input">Uncached input.</param>
/// <param name="Output">Output, reasoning included.</param>
/// <param name="CacheRead">Input read from the prompt cache.</param>
/// <param name="CacheWrite">Input written to the prompt cache with the default (five-minute) lifetime.</param>
/// <param name="CacheWrite1h">Input written to the prompt cache with a one-hour lifetime; <paramref name="CacheWrite"/> applies when missing.</param>
public sealed record ModelPrice(string Model, decimal Input, decimal Output, decimal? CacheRead = null, decimal? CacheWrite = null, [property: JsonPropertyName("cache_write_1h")] decimal? CacheWrite1h = null)
{
    /// <summary>The provider, such as <c>anthropic</c>; informational.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; init; }

    /// <summary>The official page the price was read from; informational.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>Whether <c>estimate</c> prices pending work at this model when no recorded call has used a priced model.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Estimate { get; init; }
}
