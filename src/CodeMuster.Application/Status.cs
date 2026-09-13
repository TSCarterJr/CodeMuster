using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Counts units and files in the ledger without branching on unit kind (D06), and judges coverage against the resolution threshold (D09).</summary>
public sealed class Status(ILedger ledger, Config config)
{
    /// <summary>Reads the ledger and returns the current standing.</summary>
    public async Task<StatusReport> RunAsync(CancellationToken cancellationToken)
    {
        var units = (await ledger.GetUnitsAsync(cancellationToken)).Where(u => u.Status != UnitStatus.Retired).ToList();
        var files = await ledger.GetFilesAsync(cancellationToken);
        var run = await ledger.GetLastRunAsync(cancellationToken);
        var vulnerable = (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => f.Finding.Category == DependencyFindings.Category && f.Fix?.State != FixState.Fixed)
            .GroupBy(f => f.Finding.Severity)
            .ToDictionary(group => group.Key, group => group.Count());
        var kinds = units
            .GroupBy(u => u.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new KindStatus(g.Key, g.Count(u => u.Status == UnitStatus.Done), g.Count(), g.Count(u => u.Status == UnitStatus.Stale)))
            .ToList();
        return new StatusReport(
            run?.HeadCommit,
            units.Count(u => u.Status == UnitStatus.Done),
            units.Count,
            units.Count(u => u.Status == UnitStatus.Stale),
            files.Count(f => f.ExcludedReason is not null && f.DeletedAt is null),
            units.Count(u => u.Fidelity == Fidelity.Low),
            run?.ResolutionRate,
            kinds,
            config.ResolutionThreshold,
            run?.TopUnresolvedNames,
            vulnerable);
    }
}
