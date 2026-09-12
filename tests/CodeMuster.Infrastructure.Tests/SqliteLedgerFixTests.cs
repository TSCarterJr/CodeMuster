using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerFixTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

    [Fact]
    public async Task RecordFix_StoresWhatWasFixedAndDeclined_AndMarksTheUnitDone()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var source = new Unit("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Done, Fidelity.Full, null, null, null);
        var fix = new Unit("fix:src/A.cs", UnitKind.Fix, "src/A.cs", "fp-fix", UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([source, fix], [new UnitMember(source.Id, "src/A.cs", null, "h", 0), new UnitMember(fix.Id, "src/A.cs", null, "h", 0)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(
            new Analysis(source.Id, source.Fingerprint, "lens", At, true, "A", null),
            [Finding(1), Finding(5)],
            CancellationToken.None);

        await ledger.RecordFixAsync(
            new Analysis(fix.Id, fix.Fingerprint, "lens", At, true, "fixed 1 finding(s), declined 1", null),
            [(1L, new FixOutcome(FixState.Fixed, "Added the filter.")), (2L, new FixOutcome(FixState.Declined, "Not reachable."))],
            CancellationToken.None);

        var findings = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).ToDictionary(f => f.Id, f => f.Fix);
        Assert.Equal(new FixOutcome(FixState.Fixed, "Added the filter."), findings[1]);
        Assert.Equal(new FixOutcome(FixState.Declined, "Not reachable."), findings[2]);
        Assert.Equal(UnitStatus.Done, (await ledger.GetUnitAsync(fix.Id, CancellationToken.None))!.Status);
    }

    private static Finding Finding(int line) =>
        new("src/A.cs", line, line, Severity.High, "security", "claim", "evidence", 0.9, "default");
}
