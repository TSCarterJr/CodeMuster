using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Counts units and files in the ledger without branching on unit kind (D06).</summary>
public sealed class Status(ILedger ledger)
{
    /// <summary>Reads the ledger and returns the current standing.</summary>
    public async Task<StatusReport> RunAsync(CancellationToken cancellationToken)
    {
        var units = (await ledger.GetUnitsAsync(cancellationToken)).Where(u => u.Status != UnitStatus.Retired).ToList();
        var files = await ledger.GetFilesAsync(cancellationToken);
        var run = await ledger.GetLastRunAsync(cancellationToken);
        return new StatusReport(
            run?.HeadCommit,
            units.Count(u => u.Status == UnitStatus.Done),
            units.Count,
            units.Count(u => u.Status == UnitStatus.Stale),
            files.Count(f => f.ExcludedReason is not null && f.DeletedAt is null),
            units.Count(u => u.Fidelity == Fidelity.Low),
            run?.ResolutionRate);
    }
}
