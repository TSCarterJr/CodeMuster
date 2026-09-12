using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Sizes the pending work: whole files at bytes over four, symbols at a fixed rate per line, capped for slices at the token budget (D07), plus a fixed allowance per unit for the pack scaffolding and the reply.</summary>
public sealed class Estimate(ILedger ledger, Config config, string? path = null)
{
    /// <summary>Tokens every unit costs on top of its code: the pack's instructions, sample, and response section, and the model's JSON reply.</summary>
    public const int PackOverheadTokens = 700;

    /// <summary>Tokens assumed per line of a symbol member, whose byte size the ledger does not keep.</summary>
    public const int TokensPerLine = 10;

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
        return new EstimateReport(lines, lines.Sum(line => line.Tokens));
    }

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
