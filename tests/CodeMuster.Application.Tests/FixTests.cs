using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class FixTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

    private readonly FakeLedger ledger = new();

    private Unit AddUnit(string path)
    {
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Done, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "content-" + path, 10, At, At, At, null, null, null, null, null, null);
        return unit;
    }

    private async Task<IReadOnlyList<long>> RecordAsync(Unit unit, params (int Line, Verdict? Verdict)[] findings)
    {
        var recorded = findings
            .Select(f => new Finding(unit.Key, f.Line, f.Line, Severity.High, "correctness", $"claim {f.Line}", "evidence", 0.9, "default"))
            .ToList();
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "summary", null), recorded, CancellationToken.None);
        var ids = (await ledger.GetCurrentFindingsAsync(CancellationToken.None))
            .Where(f => f.UnitId == unit.Id)
            .OrderBy(f => f.Finding.LineStart)
            .Select(f => f.Id)
            .ToList();
        foreach (var (id, verdict) in ids.Zip(findings.Select(f => f.Verdict)))
        {
            if (verdict is { } value)
            {
                ledger.Verifications[id] = new VerifyResponse(value, "because");
            }
        }

        return ids;
    }

    private Task<FixPlan> PlanAsync() => new Fix(ledger).PlanAsync(CancellationToken.None);

    private Unit Stored(string id) => ledger.Units.Single(u => u.Id == id);

    private Task<DoneResult> DoneAsync(Unit fix, string responseJson) =>
        new Done(ledger, new FakeClock(), Config.Default).RunAsync(fix.Id, fix.Fingerprint, responseJson, CancellationToken.None);

    [Fact]
    public async Task PartialFixResponse_CannotMarkTheFileComplete()
    {
        var unit = AddUnit("src/a.cs");
        var ids = await RecordAsync(unit, (10, Verdict.Confirmed), (20, Verdict.Confirmed));
        await PlanAsync();
        var fix = Stored(UnitIds.Fix(unit.Key));
        var result = await DoneAsync(fix, FixResponseJson.Serialize(new FixResponse("only one", [ids[0]], [])));
        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Empty(ledger.Fixes);
    }

    [Fact]
    public async Task AFixResponse_MarksFindingsFixedOrDeclined_AndTheUnitDone()
    {
        var unit = AddUnit("src/a.cs");
        var ids = await RecordAsync(unit, (10, Verdict.Confirmed), (20, Verdict.Confirmed));
        await PlanAsync();
        var fix = Stored(UnitIds.Fix("src/a.cs"));
        var response = FixResponseJson.Serialize(new FixResponse(
            "Added the tenant filter.",
            [ids[0]],
            [new DeclinedFix(ids[1], "The caller already checks this.")]));

        var result = await DoneAsync(fix, response);

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal("fixed 1 finding(s), declined 1", result.Message);
        Assert.Equal(UnitStatus.Done, Stored(fix.Id).Status);
        var outcomes = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).ToDictionary(f => f.Id, f => f.Fix);
        Assert.Equal(new FixOutcome(FixState.Fixed, "Added the tenant filter."), outcomes[ids[0]]);
        Assert.Equal(new FixOutcome(FixState.Declined, "The caller already checks this."), outcomes[ids[1]]);
    }

    [Fact]
    public async Task AFixResponse_CitingAFindingFromAnotherFile_IsRejected()
    {
        var a = AddUnit("src/a.cs");
        var b = AddUnit("src/b.cs");
        var mine = await RecordAsync(a, (10, Verdict.Confirmed));
        var theirs = await RecordAsync(b, (10, Verdict.Confirmed));
        await PlanAsync();
        var fix = Stored(UnitIds.Fix("src/a.cs"));
        var response = FixResponseJson.Serialize(new FixResponse("Fixed both.", [mine[0], theirs[0]], []));

        var result = await DoneAsync(fix, response);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Contains(theirs[0].ToString(System.Globalization.CultureInfo.InvariantCulture), result.Message);
    }

    [Fact]
    public async Task AFixResponse_ThatNeitherFixesNorDeclines_IsAFailedAttempt()
    {
        var unit = AddUnit("src/a.cs");
        await RecordAsync(unit, (10, Verdict.Confirmed));
        await PlanAsync();
        var fix = Stored(UnitIds.Fix("src/a.cs"));

        var result = await DoneAsync(fix, FixResponseJson.Serialize(new FixResponse("Had a look.", [], [])));

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Contains("addressed or declined", result.Message);
    }

    [Fact]
    public async Task ConfirmedFindingsInOneFile_BecomeOneFixUnitForThatFile()
    {
        var unit = AddUnit("src/a.cs");
        await RecordAsync(unit, (10, Verdict.Confirmed), (20, Verdict.Confirmed));

        var plan = await PlanAsync();

        Assert.Equal(new FixPlan(1, 2, 1), plan);
        var fix = Stored(UnitIds.Fix("src/a.cs"));
        Assert.Equal(UnitKind.Fix, fix.Kind);
        Assert.Equal("src/a.cs", fix.Key);
        Assert.Equal(UnitStatus.Pending, fix.Status);
        var member = Assert.Single(ledger.Members, m => m.UnitId == fix.Id);
        Assert.Equal(new UnitMember(fix.Id, "src/a.cs", null, "content-src/a.cs", 0), member);
    }

    [Fact]
    public async Task OnlyConfirmedFindingsAreFixed()
    {
        var unit = AddUnit("src/a.cs");
        await RecordAsync(unit, (10, Verdict.Refuted), (20, Verdict.Unsure), (30, null));

        var plan = await PlanAsync();

        Assert.Equal(new FixPlan(0, 0, 0), plan);
        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Fix);
    }

    [Fact]
    public async Task FindingsInDifferentFiles_BecomeOneUnitEach()
    {
        var a = AddUnit("src/a.cs");
        var b = AddUnit("src/b.cs");
        await RecordAsync(a, (10, Verdict.Confirmed));
        await RecordAsync(b, (5, Verdict.Confirmed), (6, Verdict.Confirmed));

        var plan = await PlanAsync();

        Assert.Equal(new FixPlan(2, 3, 2), plan);
        Assert.Equal(
            [UnitIds.Fix("src/a.cs"), UnitIds.Fix("src/b.cs")],
            ledger.Units.Where(u => u.Kind == UnitKind.Fix).Select(u => u.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AFileWithADeclinedFinding_StaysDoneWhileItsContentIsUnchanged_AndGoesStaleWhenItChanges()
    {
        var unit = AddUnit("src/a.cs");
        var ids = await RecordAsync(unit, (10, Verdict.Confirmed));
        await PlanAsync();
        var fixId = UnitIds.Fix("src/a.cs");
        await DoneAsync(Stored(fixId), FixResponseJson.Serialize(new FixResponse("left alone", [], [new DeclinedFix(ids[0], "the caller must change first")])));

        var unchanged = await PlanAsync();

        Assert.Equal(new FixPlan(1, 1, 0), unchanged);
        Assert.Equal(UnitStatus.Done, Stored(fixId).Status);

        ledger.Files["src/a.cs"] = ledger.Files["src/a.cs"] with { ContentHash = "content-after-the-fix" };
        var changed = await PlanAsync();

        Assert.Equal(new FixPlan(1, 1, 1), changed);
        Assert.Equal(UnitStatus.Stale, Stored(fixId).Status);
    }

    [Fact]
    public async Task AFixUnitWhoseFindingIsGone_IsRetired()
    {
        var unit = AddUnit("src/a.cs");
        var ids = await RecordAsync(unit, (10, Verdict.Confirmed));
        await PlanAsync();
        ledger.Verifications[ids[0]] = new VerifyResponse(Verdict.Refuted, "not real after all");

        var plan = await PlanAsync();

        Assert.Equal(new FixPlan(0, 0, 0), plan);
        Assert.Equal(UnitStatus.Retired, Stored(UnitIds.Fix("src/a.cs")).Status);
    }
}
