using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class VerifyBatchTests
{
    private const string MemberPath = "src/A.cs";

    private readonly FakeLedger ledger = new();
    private readonly FakeClock clock = new();
    private readonly FakeSourceTree tree = new FakeSourceTree().Add(MemberPath, string.Join('\n', Enumerable.Range(1, 40).Select(n => "line " + n)));
    private readonly Unit unit;

    public VerifyBatchTests()
    {
        var id = UnitIds.File(MemberPath);
        var member = new UnitMember(id, MemberPath, null, "hash-a", 0);
        unit = new Unit(id, UnitKind.File, MemberPath, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
    }

    private static Finding At(int line, string category = "security") =>
        new(MemberPath, line, line, Severity.High, category, "claim " + line, "evidence", 0.9, "default");

    private Task<DoneResult> DoneAsync(string unitId, string fingerprint, string json, Config? config = null) =>
        new Done(ledger, clock, config ?? Config.Default).RunAsync(unitId, fingerprint, json, CancellationToken.None);

    private Task<DoneResult> ReportAsync(Config? config, params Finding[] findings) =>
        DoneAsync(unit.Id, unit.Fingerprint, AnalysisResponseJson.Serialize(new AnalysisResponse("A", findings)), config);

    private Unit Verify(string id) => ledger.Units.Single(u => u.Id == id);

    private IEnumerable<string> LiveVerifyIds =>
        ledger.Units.Where(u => u.Kind == UnitKind.Verify && u.Status != UnitStatus.Retired).Select(u => u.Id).Order(StringComparer.Ordinal);

    private static string Verdicts(params (long Finding, Verdict Verdict, string Reason)[] verdicts) =>
        VerifyBatchResponseJson.Serialize(new VerifyBatchResponse(verdicts.Select(v => new FindingVerdict(v.Finding, v.Verdict, v.Reason)).ToList()));

    [Fact]
    public async Task FindingsOfOneAnalysis_ShareVerifyUnitsOfAtMostVerifyBatch()
    {
        await ReportAsync(Config.Default with { VerifyBatch = 2 }, At(3), At(9), At(20));

        Assert.Equal(["verify:1,2", "verify:3"], LiveVerifyIds);
        var batch = Verify("verify:1,2");
        Assert.Equal("src/A.cs:3 and 1 more", batch.Key);
        Assert.Equal([MemberPath], ledger.Members.Where(m => m.UnitId == batch.Id).Select(m => m.Path));
    }

    [Fact]
    public async Task TheDefaultBatchIsSix_AndVerifyBatchOneKeepsOneUnitPerFinding()
    {
        Assert.Equal(6, Config.Default.VerifyBatch);
        await ReportAsync(null, At(3), At(9), At(20));
        Assert.Equal(["verify:1,2,3"], LiveVerifyIds);

        await ReportAsync(Config.Default with { VerifyBatch = 1 }, At(3), At(9));
        Assert.Equal(["verify:4", "verify:5"], LiveVerifyIds);
    }

    [Fact]
    public void BrowserFindings_AreVerifiedAlone()
    {
        var findings = new[] { At(3), At(9, "ux_text"), At(20) }.Select((f, i) => new UnitFinding(i + 1, unit.Id, unit.Fingerprint, f, null)).ToList();

        var planned = VerifyBatches.Plan(unit, findings, ledger.Members, Fidelity.Full, 6, []);

        Assert.Equal(["verify:1,3", "verify:2"], planned.Select(p => p.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheBatchPack_ShowsTheCodeOnce_ListsEveryFindingWithItsId_AndAsksForOneVerdictEach()
    {
        await ReportAsync(null, At(3), At(9));

        var pack = (await new Next(ledger, tree, Config.Default, kind: UnitKind.Verify).RunAsync(1, CancellationToken.None)).Single();

        Assert.Equal("verify:1,2", pack.UnitId);
        Assert.Contains("\n## Findings\n", pack.Markdown);
        Assert.Contains("\"id\": 1", pack.Markdown);
        Assert.Contains("\"id\": 2", pack.Markdown);
        Assert.Contains("claim 3", pack.Markdown);
        Assert.Contains("claim 9", pack.Markdown);
        Assert.Contains(VerifyBatchResponseJson.Sample, pack.Markdown);
        Assert.Single(pack.Markdown.Split("### src/A.cs").Skip(1));
    }

    [Fact]
    public async Task ABatchResponse_RecordsEachVerdict_InOneAnalysis()
    {
        await ReportAsync(null, At(3), At(9));
        var batch = Verify("verify:1,2");
        var before = ledger.Analyses.Count;

        var result = await DoneAsync(batch.Id, batch.Fingerprint, Verdicts((1, Verdict.Confirmed, "Line 3 shows it."), (2, Verdict.Refuted, "Line 9 guards it.")));

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal("recorded 2 verdict(s): 1 confirmed, 1 refuted", result.Message);
        Assert.Equal([(1L, Verdict.Confirmed), (2L, Verdict.Refuted)], result.Verdicts.Select(v => (v.Finding, v.Verdict)));
        Assert.Equal(before + 1, ledger.Analyses.Count);
        var findings = await ledger.GetCurrentFindingsAsync(CancellationToken.None);
        Assert.Equal([Verdict.Confirmed, Verdict.Refuted], findings.Select(f => f.Verification!.Verdict));
        Assert.Equal(UnitStatus.Done, Verify(batch.Id).Status);
    }

    [Fact]
    public async Task AFindingTheResponseLeavesOut_StaysUnverified_AndGetsAVerifyUnitOfItsOwn()
    {
        await ReportAsync(null, At(3), At(9), At(20));
        var batch = Verify("verify:1,2,3");

        var result = await DoneAsync(batch.Id, batch.Fingerprint, Verdicts((1, Verdict.Confirmed, "Line 3 shows it."), (3, Verdict.Unsure, "Cannot tell from line 20.")));

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Null((await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Single(f => f.Id == 2).Verification);
        Assert.Equal(UnitStatus.Pending, Verify("verify:2").Status);
        Assert.Equal(UnitStatus.Done, Verify(batch.Id).Status);
    }

    [Theory]
    [InlineData(7, "Line 3.", "finding 7 is not in verify:1,2")]
    [InlineData(1, "  ", "every verdict needs an evidence-backed reason")]
    public async Task ABatchResponse_IsRejected_ForAFindingOutsideTheUnit_OrABlankReason(long finding, string reason, string message)
    {
        await ReportAsync(null, At(3), At(9));
        var batch = Verify("verify:1,2");

        var result = await DoneAsync(batch.Id, batch.Fingerprint, Verdicts((finding, Verdict.Confirmed, reason)));

        Assert.Equal(new DoneResult(DoneOutcome.Rejected, message), result);
        Assert.All(await ledger.GetCurrentFindingsAsync(CancellationToken.None), f => Assert.Null(f.Verification));
    }

    [Fact]
    public async Task ABatchResponseThatAnswersNothing_IsInvalid()
    {
        await ReportAsync(null, At(3), At(9));
        var batch = Verify("verify:1,2");

        var result = await DoneAsync(batch.Id, batch.Fingerprint, """{ "verdicts": [] }""");

        Assert.Equal(DoneOutcome.InvalidResponse, result.Outcome);
        Assert.Equal(UnitStatus.Failed, Verify(batch.Id).Status);
    }

    [Fact]
    public async Task Scan_RegroupsPendingPerFindingUnits_ButKeepsAVerifiedFindingsUnitDone()
    {
        var scanLedger = new FakeLedger();
        Task<ScanResult> ScanAsync() => new Scan(scanLedger, tree, new FakeContentHasher(), clock, Config.Default).RunAsync(CancellationToken.None);
        await ScanAsync();
        var scanned = scanLedger.Units.Single();
        var perFinding = Config.Default with { VerifyBatch = 1 };
        await new Done(scanLedger, clock, perFinding).RunAsync(scanned.Id, scanned.Fingerprint, AnalysisResponseJson.Serialize(new AnalysisResponse("A", [At(3), At(9), At(20)])), CancellationToken.None);
        var first = scanLedger.Units.Single(u => u.Id == "verify:1");
        await new Done(scanLedger, clock, perFinding).RunAsync(first.Id, first.Fingerprint, VerifyResponseJson.Serialize(new VerifyResponse(Verdict.Confirmed, "Line 3 shows it.")), CancellationToken.None);

        await ScanAsync();

        var live = scanLedger.Units.Where(u => u.Kind == UnitKind.Verify && u.Status != UnitStatus.Retired).ToList();
        Assert.Equal(["verify:1", "verify:2,3"], live.Select(u => u.Id).Order(StringComparer.Ordinal));
        Assert.Equal(UnitStatus.Done, live.Single(u => u.Id == "verify:1").Status);
        Assert.Equal(UnitStatus.Pending, live.Single(u => u.Id == "verify:2,3").Status);
        Assert.Equal(UnitStatus.Retired, scanLedger.Units.Single(u => u.Id == "verify:2").Status);
    }

    [Fact]
    public async Task Refresh_KeepsFindingsOfOneAnalysisTogether_AndLeavesAnAnsweredCheckDone()
    {
        await ReportAsync(Config.Default with { VerifyBatch = 1 }, At(3), At(9), At(20));
        var first = Verify("verify:1");
        await DoneAsync(first.Id, first.Fingerprint, VerifyResponseJson.Serialize(new VerifyResponse(Verdict.Confirmed, "Line 3 shows it.")));
        var hash = tree.Files.Single().KnownHash!;
        ledger.Files[MemberPath] = new FileRecord(MemberPath, Languages.CSharp, hash, 1, "t", "t", "t", null, null, null, null, null, null);

        await new RefreshVerification(ledger, tree, Config.Default, new FakeContentHasher()).RunAsync(CancellationToken.None);

        Assert.Equal(["verify:1", "verify:2,3"], LiveVerifyIds);
        Assert.Equal(UnitStatus.Pending, Verify("verify:2,3").Status);
    }

    [Fact]
    public void ConfigJson_ReadsVerifyBatch_AndRejectsLessThanOne()
    {
        Assert.Equal(3, ConfigJson.Parse("""{ "lenses": [], "verify_batch": 3 }""").VerifyBatch);
        Assert.Throws<System.Text.Json.JsonException>(() => ConfigJson.Parse("""{ "lenses": [], "verify_batch": 0 }"""));
    }
}
