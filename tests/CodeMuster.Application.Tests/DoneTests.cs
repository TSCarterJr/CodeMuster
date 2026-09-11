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

    private Task<DoneResult> RunAsync(string? unitId = null, string? fingerprint = null, string? responseJson = null) =>
        new Done(ledger, clock, Config.Default).RunAsync(unitId ?? unit.Id, fingerprint ?? unit.Fingerprint, responseJson ?? ValidResponse, CancellationToken.None);

    private Unit Stored => ledger.Units.Single(u => u.Id == unit.Id);

    [Fact]
    public async Task ValidResponse_RecordsAnalysisAndFindings_AndMarksUnitDone()
    {
        var result = await RunAsync();

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal($"recorded 1 finding(s) for {unit.Id}", result.Message);

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
        Assert.Equal($"recorded 0 finding(s) for {unit.Id}", result.Message);
        Assert.Empty(Assert.Single(ledger.Analyses).Findings);
        Assert.Equal(UnitStatus.Done, Stored.Status);
        Assert.Equal("Nothing to report.", Stored.Summary);
    }
}
