using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerProvenanceTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

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
