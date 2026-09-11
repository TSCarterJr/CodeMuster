using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Sizes the pending work: file bytes over four per unit, plus a fixed allowance per unit for the pack scaffolding and the reply.</summary>
public sealed class Estimate(ILedger ledger)
{
    /// <summary>Tokens every unit costs on top of its files: the pack's instructions, sample, and response section, and the model's JSON reply.</summary>
    public const int PackOverheadTokens = 700;

    /// <summary>Estimates every Pending, Stale, or Failed unit, grouped by kind.</summary>
    public async Task<EstimateReport> RunAsync(CancellationToken cancellationToken)
    {
        var pending = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Status is UnitStatus.Pending or UnitStatus.Stale or UnitStatus.Failed)
            .ToList();
        var members = await ledger.GetMembersAsync(pending.Select(u => u.Id).ToList(), cancellationToken);
        var sizes = (await ledger.GetFilesAsync(cancellationToken)).ToDictionary(f => f.Path, f => f.Size, StringComparer.Ordinal);
        var tokens = members
            .GroupBy(m => m.UnitId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(m => sizes.GetValueOrDefault(m.Path)) / 4, StringComparer.Ordinal);

        var lines = pending
            .GroupBy(u => u.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new EstimateLine(g.Key, g.Count(), g.Sum(u => tokens.GetValueOrDefault(u.Id) + PackOverheadTokens)))
            .ToList();
        return new EstimateReport(lines, lines.Sum(line => line.Tokens));
    }
}
