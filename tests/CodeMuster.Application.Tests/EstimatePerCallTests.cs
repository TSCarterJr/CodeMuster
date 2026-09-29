using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class EstimatePerCallTests
{
    private readonly FakeLedger ledger = new();

    private Task<EstimateReport> RunAsync(Config config) => new Estimate(ledger, config).RunAsync(CancellationToken.None);

    private static readonly Config NoVerify = Config.Default with { Verify = false };

    private void Unit(string id, UnitKind kind, UnitStatus status)
    {
        ledger.Units.Add(new Unit(id, kind, id, "fp-" + id, status, Fidelity.Full, null, null, null));
        ledger.Members.Add(new UnitMember(id, id + ".cs", null, "hash-" + id, 0));
    }

    private void Calls(int count, UnitKind kind, decimal cost, string agent = "claude", string at = "2026-09-29T10:00:00.0000000Z")
    {
        for (var i = 0; i < count; i++)
        {
            ledger.Calls.Add(new AgentCall(at, "run r", kind + ":x", kind, new AgentIdentity(agent), new AgentUsage(10, 5, 0, 0, "claude-opus-5-5", null), true, cost, "harness"));
        }
    }

    private void Analysed(string id, int findings)
    {
        Unit(id, UnitKind.File, UnitStatus.Done);
        ledger.Analyses.Add((new Analysis(id, "fp-" + id, "lens", "2026-09-29T09:00:00Z", true, "s", null),
            Enumerable.Range(1, findings).Select(n => new Finding(id + ".cs", n, n, Severity.Low, "correctness", "claim", "evidence", 0.9, "default")).ToList()));
    }

    private static string[] PerCall(EstimateReport report) =>
        report.Render().Split('\n').SkipWhile(line => !line.StartsWith("recorded cost per call", StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task EachKind_IsPricedAtItsOwnMean_OnceTwentyCallsOfThatKindAreRecorded_ElseAtTheAgentsOverallMean()
    {
        Unit("file:a", UnitKind.File, UnitStatus.Pending);
        Unit("file:b", UnitKind.File, UnitStatus.Stale);
        Unit("orphan:c", UnitKind.Orphan, UnitStatus.Pending);
        Unit("orphan:d", UnitKind.Orphan, UnitStatus.Pending);
        Unit("orphan:e", UnitKind.Orphan, UnitStatus.Failed);
        Calls(20, UnitKind.File, 0.30m);
        Calls(5, UnitKind.Slice, 0.50m);

        var report = await RunAsync(NoVerify);

        Assert.Equal(
        [
            "recorded cost per call with claude, harness overhead included:",
            "file 2 call(s) at $0.30 each, the mean of 20 file calls ~$0.60",
            "orphan 3 call(s) at $0.34 each, the mean of all 25 claude calls (fewer than 20 orphan calls recorded) ~$1.02",
            "total 5 call(s) ~$1.62 at recorded rates",
        ], PerCall(report));
    }

    [Fact]
    public async Task TheAgentUsedMostRecently_SetsTheRate()
    {
        Unit("file:a", UnitKind.File, UnitStatus.Pending);
        Calls(20, UnitKind.File, 0.01m, agent: "codex", at: "2026-09-28T10:00:00.0000000Z");
        Calls(20, UnitKind.File, 0.40m, agent: "claude", at: "2026-09-29T10:00:00.0000000Z");

        var report = await RunAsync(NoVerify);

        Assert.Equal("file 1 call(s) at $0.40 each, the mean of 20 file calls ~$0.40", PerCall(report)[1]);
        Assert.StartsWith("recorded cost per call with claude,", PerCall(report)[0]);
    }

    [Fact]
    public async Task WithVerificationOn_AddsTheVerifyCallsTheRecordedFindingRateWillCreate()
    {
        for (var i = 0; i < 20; i++) Analysed("file:done" + i, i < 10 ? 2 : 3);
        Unit("file:a", UnitKind.File, UnitStatus.Pending);
        Unit("file:b", UnitKind.File, UnitStatus.Pending);
        Unit("verify:1", UnitKind.Verify, UnitStatus.Pending);
        Unit("fix:x", UnitKind.Fix, UnitStatus.Pending);
        Calls(20, UnitKind.File, 0.30m);

        var report = await RunAsync(Config.Default);

        Assert.Equal(
        [
            "recorded cost per call with claude, harness overhead included:",
            "file 2 call(s) at $0.30 each, the mean of 20 file calls ~$0.60",
            "verify 6 call(s): 1 pending and ~5 expected at 2.5 findings per analysed unit over 20 units, at $0.30 each, the mean of all 20 claude calls (fewer than 20 verify calls recorded) ~$1.80",
            "fix 1 call(s) at $0.30 each, the mean of all 20 claude calls (fewer than 20 fix calls recorded) ~$0.30",
            "total 9 call(s) ~$2.70 at recorded rates",
        ], PerCall(report));
    }

    [Fact]
    public async Task WithFewerThanTwentyAnalysedUnits_NoVerifyCallsAreExpected()
    {
        for (var i = 0; i < 19; i++) Analysed("file:done" + i, 4);
        Unit("file:a", UnitKind.File, UnitStatus.Pending);
        Calls(20, UnitKind.File, 0.30m);

        var report = await RunAsync(Config.Default);

        Assert.Equal(
        [
            "recorded cost per call with claude, harness overhead included:",
            "file 1 call(s) at $0.30 each, the mean of 20 file calls ~$0.30",
            "total 1 call(s) ~$0.30 at recorded rates",
        ], PerCall(report));
    }

    [Fact]
    public async Task WithoutAPricedCall_ThereIsNoPerCallSection()
    {
        Unit("file:a", UnitKind.File, UnitStatus.Pending);

        Assert.Empty(PerCall(await RunAsync(Config.Default)));
    }
}
