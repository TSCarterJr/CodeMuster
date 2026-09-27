using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Recorded calls of one group: a model, a unit kind or a run.</summary>
/// <param name="Label">The model, kind or run.</param>
/// <param name="Calls">Calls in the group.</param>
/// <param name="CostUsd">Sum of the priced calls' costs.</param>
/// <param name="Unpriced">Calls without a cost.</param>
/// <param name="Tokens">Token totals of the group.</param>
public sealed record SpendGroup(string Label, int Calls, decimal CostUsd, int Unpriced, SpendTokens Tokens);

/// <summary>Token totals; a count a harness did not report adds nothing.</summary>
public sealed record SpendTokens(long Input, long Output, long CacheRead, long CacheWrite)
{
    internal static SpendTokens Of(IEnumerable<AgentCall> calls)
    {
        var list = calls.ToList();
        return new(list.Sum(c => c.Usage.InputTokens ?? 0), list.Sum(c => c.Usage.OutputTokens ?? 0),
            list.Sum(c => c.Usage.CacheReadTokens ?? 0), list.Sum(c => c.Usage.CacheWriteTokens ?? 0));
    }
}

/// <summary>What every recorded agent call cost (D63), at the costs fixed when each was recorded.</summary>
/// <param name="Calls">Every recorded call.</param>
/// <param name="Failed">Calls whose response was not recorded: the harness failed or the response was rejected.</param>
/// <param name="CostUsd">API-equivalent cost of the priced calls.</param>
/// <param name="Unpriced">Calls without a cost, never counted as zero.</param>
/// <param name="WithoutUsage">Unpriced calls whose harness reported no usage.</param>
/// <param name="UnknownModels">Models of unpriced calls that did report tokens, which had no price when the call was recorded.</param>
/// <param name="Tokens">Token totals over every call.</param>
/// <param name="ByModel">Groups by answering model, or the requested model when the harness did not say.</param>
/// <param name="ByKind">Groups by unit kind.</param>
/// <param name="ByRun">Groups by invocation, oldest first.</param>
/// <param name="BySource">Priced calls by cost source.</param>
public sealed record SpendSummary(
    int Calls, int Failed, decimal CostUsd, int Unpriced, int WithoutUsage, IReadOnlyList<string> UnknownModels, SpendTokens Tokens,
    IReadOnlyList<SpendGroup> ByModel, IReadOnlyList<SpendGroup> ByKind, IReadOnlyList<SpendGroup> ByRun, IReadOnlyDictionary<string, int> BySource);

/// <summary>Summarizes and formats agent-call spend; the figures are API-equivalent whether or not the harness ran on a subscription (D63).</summary>
public static class Spend
{
    /// <summary>The summary of <paramref name="calls"/>, or null when none was recorded.</summary>
    public static SpendSummary? Summarize(IReadOnlyList<AgentCall> calls)
    {
        if (calls.Count == 0) return null;
        var unpriced = calls.Where(c => c.CostUsd is null).ToList();
        return new SpendSummary(
            calls.Count,
            calls.Count(c => !c.Succeeded),
            calls.Sum(c => c.CostUsd ?? 0),
            unpriced.Count,
            unpriced.Count(c => !c.Usage.HasTokens),
            unpriced.Where(c => c.Usage.HasTokens).Select(ModelOf).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            SpendTokens.Of(calls),
            Groups(calls, ModelOf).OrderBy(g => g.Label, StringComparer.Ordinal).ToList(),
            Groups(calls, c => c.Kind is { } kind ? kind.ToString().ToLowerInvariant() : "outside units").OrderBy(g => g.Label, StringComparer.Ordinal).ToList(),
            Groups(calls, c => c.Run).ToList(),
            calls.Where(c => c.CostSource is not null).GroupBy(c => c.CostSource!, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }

    /// <summary>The model a call is attributed to: the one that answered, else the one requested, else the harness default.</summary>
    public static string ModelOf(AgentCall call) => call.Usage.Model ?? call.By.Model ?? call.By.Agent + " default";

    /// <summary>US dollars with cents, or four decimals below one cent so small runs do not read as free.</summary>
    public static string Money(decimal value) =>
        "$" + (value is > 0 and < 0.01m ? value.ToString("0.0000", CultureInfo.InvariantCulture) : value.ToString("0.00", CultureInfo.InvariantCulture));

    /// <summary>A group's cost, saying how many of its calls are unpriced.</summary>
    public static string Cost(SpendGroup group) =>
        group.Unpriced == group.Calls ? "unpriced"
        : group.Unpriced == 0 ? Money(group.CostUsd)
        : string.Create(CultureInfo.InvariantCulture, $"{Money(group.CostUsd)} + {group.Unpriced} unpriced");

    /// <summary>One call's tokens and cost, such as <c>1200 input, 300 output tokens, $0.0081 API-equivalent (table 2026-09-27)</c>.</summary>
    public static string Describe(AgentUsage usage, decimal? costUsd, string? source)
    {
        var tokens = usage.HasTokens
            ? string.Create(CultureInfo.InvariantCulture, $"{usage.InputTokens ?? 0} input, {usage.OutputTokens ?? 0} output, {usage.CacheReadTokens ?? 0} cache read, {usage.CacheWriteTokens ?? 0} cache write tokens")
            : "usage not reported";
        var model = usage.Model is null ? "" : " by " + usage.Model;
        return tokens + model + ", " + (costUsd is { } cost ? $"{Money(cost)} API-equivalent ({source})" : "unpriced");
    }

    private static IEnumerable<SpendGroup> Groups(IEnumerable<AgentCall> calls, Func<AgentCall, string> key) =>
        calls.GroupBy(key, StringComparer.Ordinal).Select(g => new SpendGroup(
            g.Key, g.Count(), g.Sum(c => c.CostUsd ?? 0), g.Count(c => c.CostUsd is null), SpendTokens.Of(g)));
}
