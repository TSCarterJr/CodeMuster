using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerProvenanceTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

    [Fact]
    public async Task FailureHistory_SurvivesReopenAndSuccess_InInsertionOrder_WithoutRetiredUnits()
    {
        using var temp = new TempDirectory();
        var unit = new Unit("fix:a.cs", UnitKind.Fix, "a.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null);
        var retired = unit with { Id = "fix:old.cs", Key = "old.cs" };
        var failure = new Analysis(unit.Id, unit.Fingerprint, "lens", At, false, null, "compiler error\nline 10", new AgentIdentity("codex", "model", "effort"));
        var second = failure with { Error = "extra file b.cs" };
        using (var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await ledger.UpsertUnitsAsync([unit, retired], [], CancellationToken.None);
            await ledger.RecordAnalysisAsync(failure, [], CancellationToken.None);
            await ledger.RecordAnalysisAsync(second, [], CancellationToken.None);
            await ledger.RecordAnalysisAsync(failure with { UnitId = retired.Id }, [], CancellationToken.None);
            await ledger.RecordFixAsync(failure with { Succeeded = true, Summary = "fixed", Error = null }, [], CancellationToken.None);
            await ledger.UpsertUnitsAsync([retired with { Status = UnitStatus.Retired }], [], CancellationToken.None);
        }
        using var reopened = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        Assert.Equal(new[] { failure, second }, await reopened.GetFailedAnalysesAsync(CancellationToken.None));
        Assert.Equal(UnitStatus.Done, (await reopened.GetUnitAsync(unit.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task LatestSuccessfulAnalysisPerUnit_KeepsTheAgentModelAndEffort()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = new Unit("file:src/a.cs", UnitKind.File, "src/a.cs", "fp-a", UnitStatus.Pending, Fidelity.Full, null, null, null);
        var other = new Unit("file:src/b.cs", UnitKind.File, "src/b.cs", "fp-b", UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([unit, other], [], CancellationToken.None);

        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, false, null, "bad json", new AgentIdentity("codex", "luna", "low")), [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "A", null, new AgentIdentity("claude", "opus", "xhigh")), [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(other.Id, other.Fingerprint, "lens", At, true, "B", null, null), [], CancellationToken.None);

        var provenance = await ledger.GetProvenanceAsync(CancellationToken.None);

        Assert.Equal(new AgentIdentity("claude", "opus", "xhigh"), provenance[unit.Id]);
        Assert.False(provenance.ContainsKey(other.Id));
    }
}
