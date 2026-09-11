using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerFindingsTests
{
    private const string At = "2026-09-10T05:00:00.0000000Z";

    [Fact]
    public async Task GetCurrentFindings_EmptyLedger_ReturnsEmpty()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Empty(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentFindings_ReturnsOnlyTheLatestSucceededAnalysisOfEachUnit()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var a = Pending("src/A.cs");
        var b = Pending("src/B.cs");
        await ledger.UpsertUnitsAsync([a, b], [Member(a), Member(b)], CancellationToken.None);
        var old = Finding("src/A.cs", 1);
        var a1 = Finding("src/A.cs", 2);
        var a2 = Finding("src/A.cs", 3);
        var b1 = Finding("src/B.cs", 4);
        await ledger.RecordAnalysisAsync(new Analysis(a.Id, "fp-a1", "lens", At, true, "Old A", null), [old], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(a.Id, "fp-a2", "lens", At, true, "New A", null), [a1, a2], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(b.Id, "fp-b", "lens", At, true, "B", null), [b1], CancellationToken.None);

        var findings = await ledger.GetCurrentFindingsAsync(CancellationToken.None);

        Assert.Equal([new UnitFinding(2, a.Id, "fp-a2", a1, null), new UnitFinding(3, a.Id, "fp-a2", a2, null), new UnitFinding(4, b.Id, "fp-b", b1, null)], findings);
    }

    [Fact]
    public async Task GetCurrentFindings_KeepsTheFingerprintTheAnalysisWasMadeAgainst_WhenTheUnitHasMovedOn()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = Pending("src/A.cs");
        await ledger.UpsertUnitsAsync([unit], [Member(unit)], CancellationToken.None);
        var finding = Finding("src/A.cs", 1);
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, "fp-old", "lens", At, true, "A", null), [finding], CancellationToken.None);
        await ledger.UpsertUnitsAsync([unit with { Fingerprint = "fp-new", Status = UnitStatus.Stale }], [Member(unit)], CancellationToken.None);

        Assert.Equal([new UnitFinding(1, unit.Id, "fp-old", finding, null)], await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentFindings_ExcludesRetiredUnits()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var gone = Pending("src/Gone.cs");
        var kept = Pending("src/Kept.cs");
        await ledger.UpsertUnitsAsync([gone, kept], [Member(gone), Member(kept)], CancellationToken.None);
        var keptFinding = Finding("src/Kept.cs", 1);
        await ledger.RecordAnalysisAsync(new Analysis(gone.Id, gone.Fingerprint, "lens", At, true, "Gone", null), [Finding("src/Gone.cs", 1)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(kept.Id, kept.Fingerprint, "lens", At, true, "Kept", null), [keptFinding], CancellationToken.None);
        await ledger.UpsertUnitsAsync([gone with { Status = UnitStatus.Retired }], [Member(gone)], CancellationToken.None);

        Assert.Equal([new UnitFinding(2, kept.Id, kept.Fingerprint, keptFinding, null)], await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentFindings_FailedAnalysesNeverContribute()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var a = Pending("src/A.cs");
        var b = Pending("src/B.cs");
        await ledger.UpsertUnitsAsync([a, b], [Member(a), Member(b)], CancellationToken.None);
        var good = Finding("src/A.cs", 1);
        await ledger.RecordAnalysisAsync(new Analysis(a.Id, "fp-a1", "lens", At, true, "A", null), [good], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(a.Id, "fp-a2", "lens", At, false, null, "invalid json"), [Finding("src/A.cs", 2)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(b.Id, "fp-b", "lens", At, false, null, "invalid json"), [Finding("src/B.cs", 3)], CancellationToken.None);

        Assert.Equal([new UnitFinding(1, a.Id, "fp-a1", good, null)], await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordVerification_StoresTheVerdictOnTheFinding_AndMarksTheVerifyUnitDone()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = Pending("src/A.cs");
        await ledger.UpsertUnitsAsync([unit], [Member(unit)], CancellationToken.None);
        var finding = Finding("src/A.cs", 1);
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "A", null), [finding, Finding("src/A.cs", 5)], CancellationToken.None);
        var verify = new Unit(UnitIds.Verify(1), UnitKind.Verify, "src/A.cs:1-2", unit.Fingerprint, UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([verify], [Member(unit) with { UnitId = verify.Id }], CancellationToken.None);
        var refuted = new VerifyResponse(Verdict.Refuted, "Line 1 already filters by tenant.");

        await ledger.RecordVerificationAsync(new Analysis(verify.Id, verify.Fingerprint, "lens", At, true, "refuted: Line 1 already filters by tenant.", null), 1, refuted, CancellationToken.None);

        var findings = await ledger.GetCurrentFindingsAsync(CancellationToken.None);
        Assert.Equal([new UnitFinding(1, unit.Id, unit.Fingerprint, finding, refuted), new UnitFinding(2, unit.Id, unit.Fingerprint, Finding("src/A.cs", 5), null)], findings);
        var stored = await ledger.GetUnitAsync(verify.Id, CancellationToken.None);
        Assert.Equal(verify with { Status = UnitStatus.Done, LensHash = "lens", Summary = "refuted: Line 1 already filters by tenant.", SummaryHash = verify.Fingerprint }, stored);
    }

    [Fact]
    public async Task RecordVerification_Again_ReplacesTheVerdict()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = Pending("src/A.cs");
        await ledger.UpsertUnitsAsync([unit], [Member(unit)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "A", null), [Finding("src/A.cs", 1)], CancellationToken.None);
        var verify = new Unit(UnitIds.Verify(1), UnitKind.Verify, "src/A.cs:1-2", unit.Fingerprint, UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([verify], [Member(unit) with { UnitId = verify.Id }], CancellationToken.None);
        var analysis = new Analysis(verify.Id, verify.Fingerprint, "lens", At, true, "verdict", null);

        await ledger.RecordVerificationAsync(analysis, 1, new VerifyResponse(Verdict.Unsure, "first"), CancellationToken.None);
        await ledger.RecordVerificationAsync(analysis, 1, new VerifyResponse(Verdict.Confirmed, "second"), CancellationToken.None);

        Assert.Equal(new VerifyResponse(Verdict.Confirmed, "second"), Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Verification);
    }

    private static Unit Pending(string path) =>
        new(UnitIds.File(path), UnitKind.File, path, "fp-" + path, UnitStatus.Pending, Fidelity.Full, null, null, null);

    private static UnitMember Member(Unit unit) => new(unit.Id, unit.Key, null, "hash-" + unit.Key, 0);

    private static Finding Finding(string path, int line) =>
        new(path, line, line + 1, Severity.High, "security", "claim " + line, "evidence " + line, 0.9, "lens-a");
}
