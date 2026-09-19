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
        var uxUnits = units.Where(u => u.Kind == UnitKind.Ux).ToList();
        var receipts = config.UserExperience.Enabled ? await ledger.GetLatestEvidenceAsync(cancellationToken) : new Dictionary<string, Analysis>();
        var reviewed = uxUnits.Count(u => u.Status == UnitStatus.Done && receipts.TryGetValue(u.Id, out var receipt)
            && receipt.Fingerprint == u.Fingerprint && receipt.LensHash == Config.HashOf(config.LensesFor([(u.Key, Languages.FromPath(u.Key))])));
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
            vulnerable)
        {
            Skipped = units.Count(u => u.Status == UnitStatus.Skipped),
            UxStatus = !config.UserExperience.Enabled ? null : run is null ? "UX: no scan yet" : uxUnits.Count == 0 ? "UX: not applicable (no UI targets in the configured scope)"
                : $"UX: browser evidence recorded for {reviewed}/{uxUnits.Count} UI targets; {uxUnits.Count - reviewed} incomplete",
            UxIncomplete = config.UserExperience.Enabled && reviewed < uxUnits.Count,
        };
    }
}
