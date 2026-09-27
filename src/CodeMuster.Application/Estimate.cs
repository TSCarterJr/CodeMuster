using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Sizes the pending work: whole files at bytes over four, symbols at a fixed rate per line, capped for slices at the token budget (D07), plus a fixed allowance per unit for the pack scaffolding and the reply. Prices it per model at API rates, with output tokens assumed from recorded history or <see cref="DefaultOutputRatio"/> (D63).</summary>
public sealed class Estimate(ILedger ledger, Config config, string? path = null)
{
    /// <summary>Tokens every unit costs on top of its code: the pack's instructions, sample, and response section, and the model's JSON reply.</summary>
    public const int PackOverheadTokens = 700;

    /// <summary>Tokens assumed per line of a symbol member, whose byte size the ledger does not keep.</summary>
    public const int TokensPerLine = 10;

    /// <summary>Output tokens assumed per input token until enough calls are recorded to measure it (D63).</summary>
    public const decimal DefaultOutputRatio = 0.10m;

    /// <summary>Recorded calls with usage needed before the output ratio is measured from history instead of assumed.</summary>
    public const int CallsForMeasuredRatio = 20;

    /// <summary>Estimates every Pending, Stale, or Failed unit, grouped by kind.</summary>
    public async Task<EstimateReport> RunAsync(CancellationToken cancellationToken)
    {
        var pending = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Status is UnitStatus.Pending or UnitStatus.Stale or UnitStatus.Failed)
            .ToList();
        var members = await ledger.GetMembersAsync(pending.Select(u => u.Id).ToList(), cancellationToken);
        if (path is { } folder)
        {
            var under = members
                .Where(m => m.Path == RepoPath.Normalize(folder).TrimEnd('/') || m.Path.StartsWith(RepoPath.Normalize(folder).TrimEnd('/') + "/", StringComparison.Ordinal))
                .Select(m => m.UnitId)
                .ToHashSet(StringComparer.Ordinal);
            pending = pending.Where(u => under.Contains(u.Id)).ToList();
            members = members.Where(m => under.Contains(m.UnitId)).ToList();
        }

        var sizes = (await ledger.GetFilesAsync(cancellationToken)).ToDictionary(f => f.Path, f => f.Size, StringComparer.Ordinal);
        var tokens = members
            .GroupBy(m => m.UnitId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => UnitTokens(g.ToList(), sizes), StringComparer.Ordinal);

        var lines = pending
            .GroupBy(u => u.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new EstimateLine(g.Key, g.Count(), g.Sum(u => tokens.GetValueOrDefault(u.Id) + PackOverheadTokens)))
            .ToList();
        var total = lines.Sum(line => line.Tokens);
        var calls = await ledger.GetAgentCallsAsync(cancellationToken);
        var prices = PriceTable.For(config);
        var measured = calls.Where(c => c.Usage.OutputTokens is not null && InputOf(c.Usage) > 0).ToList();
        var ratio = measured.Count >= CallsForMeasuredRatio
            ? (decimal)measured.Sum(c => c.Usage.OutputTokens!.Value) / measured.Sum(c => InputOf(c.Usage))
            : DefaultOutputRatio;
        var output = (long)Math.Round(total * ratio, MidpointRounding.AwayFromZero);
        var models = calls.Select(c => prices.Find(c.Usage.Model ?? c.By.Model)?.Price)
            .Concat(prices.Overrides)
            .OfType<ModelPrice>()
            .DistinctBy(price => price.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (models.Count == 0) models = prices.Bundled.Where(price => price.Estimate).ToList();
        var priced = calls.Where(c => c.CostUsd is not null).ToList();
        return new EstimateReport(lines, total)
        {
            OutputTokens = output,
            OutputRatio = ratio,
            MeasuredCalls = measured.Count >= CallsForMeasuredRatio ? measured.Count : 0,
            Costs = total == 0 ? [] : models
                .Select(price => new EstimateCost(price.Model, (total * price.Input + output * price.Output) / 1_000_000m))
                .OrderBy(cost => cost.Model, StringComparer.Ordinal)
                .ToList(),
            RecordedAverageUsd = priced.Count == 0 ? null : priced.Sum(c => c.CostUsd!.Value) / priced.Count,
            RecordedCalls = priced.Count,
            PendingUnits = lines.Sum(line => line.Units),
        };
    }

    private static long InputOf(AgentUsage usage) => (usage.InputTokens ?? 0) + (usage.CacheReadTokens ?? 0) + (usage.CacheWriteTokens ?? 0);

    private long UnitTokens(IReadOnlyList<UnitMember> members, Dictionary<string, long> sizes)
    {
        long Tokens(IEnumerable<UnitMember> part) =>
            part.Where(m => m.Range is null).Sum(m => sizes.GetValueOrDefault(m.Path)) / 4
            + part.Sum(m => m.Range is { } range ? (range.EndLine - range.StartLine + 1L) * TokensPerLine : 0);

        var total = Tokens(members);
        return members.Any(m => m.Distance > 0)
            ? Math.Min(total, Math.Max(config.SliceTokenBudget, Tokens(members.Where(m => m.Distance == 0))))
            : total;
    }
}
