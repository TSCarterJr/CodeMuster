using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerFixTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

    [Fact]
    public async Task ResolvedVerification_PersistsFixedOutcome_AndConfirmationReopensFixUnit()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var source = new Unit("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Done, Fidelity.Full, null, null, null);
        var fix = source with { Id = UnitIds.Fix(source.Key), Kind = UnitKind.Fix };
        var verify = source with { Id = UnitIds.Verify(1), Kind = UnitKind.Verify };
        await ledger.UpsertUnitsAsync([source, fix, verify], [], CancellationToken.None);
        var analysis = new Analysis(source.Id, "fp", "lens", At, true, "source", null);
        await ledger.RecordAnalysisAsync(analysis, [Finding(1)], CancellationToken.None);
        await ledger.RecordFixAsync(analysis with { UnitId = fix.Id }, [(1L, new FixOutcome(FixState.Declined, "needs caller edit"))], CancellationToken.None);
        await ledger.RecordVerificationAsync(analysis with { UnitId = verify.Id }, 1, new VerifyResponse(Verdict.Resolved, "caller repaired"), CancellationToken.None);
        Assert.Equal(new FixOutcome(FixState.Fixed, "caller repaired"), Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
        await ledger.RecordVerificationAsync(analysis with { UnitId = verify.Id }, 1, new VerifyResponse(Verdict.Confirmed, "regression"), CancellationToken.None);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
        Assert.Equal(UnitStatus.Stale, (await ledger.GetUnitAsync(fix.Id, CancellationToken.None))!.Status);
    }

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
