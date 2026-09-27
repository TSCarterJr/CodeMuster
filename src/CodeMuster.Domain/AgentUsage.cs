namespace CodeMuster.Domain;

/// <summary>What one agent call used, as the harness reported it (D63). Every field is null when the harness did not report it. Input tokens exclude cache reads and cache writes, and output tokens include reasoning, so each count is billed at exactly one rate.</summary>
/// <param name="InputTokens">Uncached input tokens.</param>
/// <param name="OutputTokens">Output tokens, reasoning included.</param>
/// <param name="CacheReadTokens">Input tokens read from the prompt cache.</param>
/// <param name="CacheWriteTokens">Input tokens written to the prompt cache, for any cache lifetime.</param>
/// <param name="Model">The model that actually answered, verbatim from the harness.</param>
/// <param name="ReportedCostUsd">The cost the harness itself reported for the call, in US dollars.</param>
public sealed record AgentUsage(long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheWriteTokens, string? Model, decimal? ReportedCostUsd)
{
    /// <summary>The part of <see cref="AgentUsage.CacheWriteTokens"/> written with a one-hour lifetime, which Anthropic bills at a higher rate than the five-minute default; null when the harness did not split it.</summary>
    public long? CacheWrite1hTokens { get; init; }

    /// <summary>A call whose usage the harness did not report.</summary>
    public static AgentUsage Unknown { get; } = new(null, null, null, null, null, null);

    /// <summary>True when the harness reported any token count.</summary>
    public bool HasTokens => InputTokens is not null || OutputTokens is not null || CacheReadTokens is not null || CacheWriteTokens is not null;
}
