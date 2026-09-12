using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DoneTests
{
    private const string MemberPath = "src/A.cs";
    private static readonly string ValidResponse = AnalysisResponseJson.Sample.Replace("src/Billing/InvoiceRepository.cs", MemberPath, StringComparison.Ordinal);

    private readonly FakeLedger ledger = new();
    private readonly FakeClock clock = new();
    private readonly Unit unit;

    public DoneTests()
    {
        var id = UnitIds.File(MemberPath);
        var member = new UnitMember(id, MemberPath, null, "hash-a", 0);
        unit = new Unit(id, UnitKind.File, MemberPath, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
    }

    private Task<DoneResult> RunAsync(string? unitId = null, string? fingerprint = null, string? responseJson = null, Config? config = null) =>
        new Done(ledger, clock, config ?? Config.Default).RunAsync(unitId ?? unit.Id, fingerprint ?? unit.Fingerprint, responseJson ?? ValidResponse, CancellationToken.None);

    private Unit Stored => ledger.Units.Single(u => u.Id == unit.Id);

    private static string Respond(params (int Start, int End)[] ranges) => AnalysisResponseJson.Serialize(new AnalysisResponse("A",
        ranges.Select(r => new Finding(MemberPath, r.Start, r.End, Severity.High, "security", "claim", "evidence", 0.9, "default")).ToList()));

    private async Task<Unit> RecordOneFindingAsync()
    {
        await RunAsync(responseJson: Respond((18, 21)));
        return ledger.Units.Single(u => u.Id == UnitIds.Verify(1));
    }

    [Fact]
    public async Task RecordedAnalysis_KeepsTheAgentModelAndEffortThatProducedIt()
    {
        var by = new AgentIdentity("codex", "luna", "low");

        var result = await new Done(ledger, clock, Config.Default, by).RunAsync(unit.Id, unit.Fingerprint, ValidResponse, CancellationToken.None);

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal(by, Assert.Single(ledger.Analyses).Analysis.By);
    }

    [Fact]
    public async Task AFailedAnalysis_KeepsItsProvenanceToo()
    {
        var by = new AgentIdentity("claude", null, "max");

        await new Done(ledger, clock, Config.Default, by).RunAsync(unit.Id, unit.Fingerprint, "not json", CancellationToken.None);

        Assert.Equal(by, Assert.Single(ledger.Analyses).Analysis.By);
    }

    [Fact]
    public async Task RecordedFindings_EachGetAPendingVerifyUnit_WithTheUnitsMembers()
    {
        var result = await RunAsync(responseJson: Respond((18, 21), (30, 30)));

        Assert.Equal("recorded 2 finding(s)", result.Message);
        var verify = ledger.Units.Where(u => u.Kind == UnitKind.Verify).ToList();
        Assert.Equal(
            [
                new Unit(UnitIds.Verify(1), UnitKind.Verify, "src/A.cs:18-21", unit.Fingerprint, UnitStatus.Pending, Fidelity.Full, null, null, null),
                new Unit(UnitIds.Verify(2), UnitKind.Verify, "src/A.cs:30", unit.Fingerprint, UnitStatus.Pending, Fidelity.Full, null, null, null),
            ],
            verify);
        Assert.Equal([new UnitMember(UnitIds.Verify(1), MemberPath, null, "hash-a", 0)], ledger.Members.Where(m => m.UnitId == UnitIds.Verify(1)));
    }

    [Fact]
    public async Task VerifyOff_RecordsTheFindings_WithoutVerifyUnits()
    {
        var result = await RunAsync(responseJson: Respond((18, 21)), config: Config.Default with { Verify = false });

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Single(ledger.Analyses.Single().Findings);
        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Verify);
    }

    [Fact]
    public async Task Reanalysis_RetiresTheOldVerifyUnitsBeforeRecording_SoNoOtherWriterCanHandOutOneWhoseFindingIsGone()
    {
        await RecordOneFindingAsync();
        UnitStatus? whenRecorded = null;
        ledger.OnRecordAnalysis = _ => whenRecorded = ledger.Units.Single(u => u.Id == UnitIds.Verify(1)).Status;

        await RunAsync(responseJson: Respond((18, 21)));

        Assert.Equal(UnitStatus.Retired, whenRecorded);
    }

    [Fact]
    public async Task NoFindings_NoVerifyUnits()
    {
        await RunAsync(responseJson: Respond());

        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Verify);
    }

    [Fact]
    public async Task Reanalysis_RetiresTheVerifyUnitsOfTheFindingsItReplaces_AndKeepsTheirRows()
    {
        await RecordOneFindingAsync();

        await RunAsync(responseJson: Respond((18, 21)));

        Assert.Equal(UnitStatus.Retired, ledger.Units.Single(u => u.Id == UnitIds.Verify(1)).Status);
        Assert.Equal(UnitStatus.Pending, ledger.Units.Single(u => u.Id == UnitIds.Verify(2)).Status);
        Assert.Single(ledger.Members, m => m.UnitId == UnitIds.Verify(1));
    }

    [Theory]
    [InlineData(Verdict.Confirmed, "confirmed")]
    [InlineData(Verdict.Refuted, "refuted")]
    [InlineData(Verdict.Unsure, "unsure")]
    public async Task Verdict_IsStoredOnTheFinding_AndTheVerifyUnitIsDone(Verdict verdict, string name)
    {
        var verify = await RecordOneFindingAsync();
        var response = new VerifyResponse(verdict, "Line 19 settles it.");

        var result = await RunAsync(verify.Id, verify.Fingerprint, VerifyResponseJson.Serialize(response));

        Assert.Equal(new DoneResult(DoneOutcome.Recorded, $"recorded {name}"), result);
        Assert.Equal(response, ledger.Verifications[1]);
        Assert.Equal(
            verify with { Status = UnitStatus.Done, Summary = $"{name}: Line 19 settles it.", SummaryHash = verify.Fingerprint, LensHash = Config.HashOf(Config.Default.Lenses) },
            ledger.Units.Single(u => u.Id == verify.Id));
        Assert.Empty(ledger.Analyses[^1].Findings);
        Assert.Equal(response, Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Verification);
    }

    [Fact]
    public async Task VerifyUnit_GivenAnAnalysisInsteadOfAVerdict_IsFailed_AndTheFindingStaysUnverified()
    {
        var verify = await RecordOneFindingAsync();

        var result = await RunAsync(verify.Id, verify.Fingerprint, ValidResponse);

        Assert.Equal(DoneOutcome.InvalidResponse, result.Outcome);
        Assert.Equal(UnitStatus.Failed, ledger.Units.Single(u => u.Id == verify.Id).Status);
        Assert.Empty(ledger.Verifications);
    }

    [Fact]
    public async Task VerifyUnit_WhoseFindingWasReplaced_IsRejected()
    {
        var verify = await RecordOneFindingAsync();
        await RunAsync(responseJson: Respond((18, 21)));

        var result = await RunAsync(verify.Id, verify.Fingerprint, VerifyResponseJson.Sample);

        Assert.Equal(new DoneResult(DoneOutcome.Rejected, "verify:1 tests a finding that a later analysis replaced; run codemuster scan"), result);
        Assert.Empty(ledger.Verifications);
    }

    [Fact]
    public async Task ValidResponse_RecordsAnalysisAndFindings_AndMarksUnitDone()
    {
        var result = await RunAsync();

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal("recorded 1 finding(s)", result.Message);

        var expected = AnalysisResponseJson.Parse(ValidResponse);
        var (analysis, findings) = Assert.Single(ledger.Analyses);
        Assert.Equal(new Analysis(unit.Id, unit.Fingerprint, Config.HashOf(Config.Default.Lenses), Timestamps.Format(clock.UtcNow), true, expected.Summary, null), analysis);
        Assert.Equal(expected.Findings, findings);

        Assert.Equal(UnitStatus.Done, Stored.Status);
        Assert.Equal(expected.Summary, Stored.Summary);
        Assert.Equal(unit.Fingerprint, Stored.SummaryHash);
        Assert.Equal(Config.HashOf(Config.Default.LensesFor([(MemberPath, Languages.CSharp)])), Stored.LensHash);
    }

    [Fact]
    public async Task DotSlashPath_IsNormalized_AndRecorded()
    {
        var result = await RunAsync(responseJson: ValidResponse.Replace($"\"{MemberPath}\"", $"\"./{MemberPath}\"", StringComparison.Ordinal));

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal(MemberPath, ledger.Analyses.Single().Findings.Single().Path);
    }

    [Fact]
    public async Task FindingOutsideUnit_IsRejected_AndNothingIsRecorded()
    {
        var response = ValidResponse.Replace(MemberPath, "src/B.cs", StringComparison.Ordinal);

        var result = await RunAsync(responseJson: response);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Equal($"finding cites src/B.cs, which is not in unit {unit.Id}", result.Message);
        Assert.Empty(ledger.Analyses);
        Assert.Equal(UnitStatus.Pending, Stored.Status);
    }

    [Fact]
    public async Task InvalidJson_RecordsFailedAnalysis_AndMarksUnitFailed()
    {
        var result = await RunAsync(responseJson: "{ not json");

        Assert.Equal(DoneOutcome.InvalidResponse, result.Outcome);
        Assert.StartsWith("invalid response: ", result.Message);

        var (analysis, findings) = Assert.Single(ledger.Analyses);
        Assert.False(analysis.Succeeded);
        Assert.NotNull(analysis.Error);
        Assert.Equal("invalid response: " + analysis.Error, result.Message);
        Assert.Null(analysis.Summary);
        Assert.Equal(unit.Fingerprint, analysis.Fingerprint);
        Assert.Equal(Timestamps.Format(clock.UtcNow), analysis.CreatedAt);
        Assert.Empty(findings);

        Assert.Equal(UnitStatus.Failed, Stored.Status);
        Assert.Null(Stored.Summary);
    }

    [Fact]
    public async Task SchemaViolation_IsInvalidResponse_NotRecordedAsSuccess()
    {
        var result = await RunAsync(responseJson: """{ "summary": "no findings key" }""");

        Assert.Equal(DoneOutcome.InvalidResponse, result.Outcome);
        Assert.False(Assert.Single(ledger.Analyses).Analysis.Succeeded);
        Assert.Equal(UnitStatus.Failed, Stored.Status);
    }

    [Fact]
    public async Task WrongFingerprint_IsRejected_AndNothingIsRecorded()
    {
        var result = await RunAsync(fingerprint: "stale-fingerprint");

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Equal($"unit {unit.Id} changed since next; run next again", result.Message);
        Assert.Empty(ledger.Analyses);
        Assert.Equal(UnitStatus.Pending, Stored.Status);
    }

    [Fact]
    public async Task UnknownUnit_IsRejected()
    {
        var result = await RunAsync(unitId: "file:src/Missing.cs");

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Equal("unknown unit file:src/Missing.cs", result.Message);
        Assert.Empty(ledger.Analyses);
    }

    [Fact]
    public async Task EmptyFindings_IsRecorded_AndUnitIsDone()
    {
        var result = await RunAsync(responseJson: """{ "summary": "Nothing to report.", "findings": [] }""");

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal("recorded 0 finding(s)", result.Message);
        Assert.Empty(Assert.Single(ledger.Analyses).Findings);
        Assert.Equal(UnitStatus.Done, Stored.Status);
        Assert.Equal("Nothing to report.", Stored.Summary);
    }
}
