using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class SpendRecordingTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";
    private const string EmptyResponse = """{"summary": "Nothing to report.", "findings": []}""";
    private static readonly AgentUsage Sonnet = new(1_000_000, 100_000, 0, 0, "claude-sonnet-4-6", null);

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeWorkspace workspace = new();

    private Unit AddFileUnit(string path)
    {
        tree.Add(path, $"// {path}\nclass A {{ }}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "content-" + path, 10, At, At, At, null, null, null, null, null, null);
        return unit;
    }

    private Task<RunResult> RunAsync(IAgentAdapter adapter, Config? config = null, int attempts = 1) =>
        new Run(ledger, tree, clock, (config ?? Config.Default) with { BatchUnits = 1 }, adapter, new Progress<RunProgress>()).RunAsync(new RunOptions(1, attempts, false), CancellationToken.None);

    [Fact]
    public async Task Run_RecordsEachCall_WithItsUsageAndACostFixedFromTheTable()
    {
        var unit = AddFileUnit("src/a.cs");
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)) { Usage = Sonnet, Identity = new AgentIdentity("claude", "sonnet", "high") };

        await RunAsync(adapter);

        var call = Assert.Single(ledger.Calls);
        Assert.Equal(new AgentCall(At2(clock), "run " + At2(clock), unit.Id, UnitKind.File, adapter.Identity, Sonnet, true, 4.5m, "table " + PriceTable.Default.Checked), call);
    }

    [Fact]
    public async Task Run_RecordsACallThatFailedAfterTheHarnessRan_WithTheUsageItReported()
    {
        var unit = AddFileUnit("src/a.cs");
        var adapter = new FakeAgentAdapter((_, _) => throw new AgentCallException("claude reported an error: max turns", Sonnet with { ReportedCostUsd = 0.31m }));

        await RunAsync(adapter);

        var call = Assert.Single(ledger.Calls);
        Assert.Equal((unit.Id, false, 0.31m, "harness"), (call.UnitId, call.Succeeded, call.CostUsd, call.CostSource));
    }

    [Fact]
    public async Task Run_DoesNotRecordACall_WhenTheHarnessNeverStarted()
    {
        AddFileUnit("src/a.cs");

        await RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException("claude is not installed")));

        Assert.Empty(ledger.Calls);
    }

    [Fact]
    public async Task Run_RecordsInvalidAndRejectedResponses_AsFailedCalls_EveryAttempt()
    {
        AddFileUnit("src/a.cs");
        AddFileUnit("src/b.cs");
        var responses = new Queue<string>(["not json", """{"summary": "s", "findings": [{"path": "elsewhere.cs", "line_start": 1, "line_end": 1, "severity": "low", "category": "c", "claim": "x", "evidence": "y", "confidence": 0.9, "lens_id": "default"}]}"""]);
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(responses.Dequeue())) { Usage = Sonnet };

        await RunAsync(adapter);

        Assert.Equal([false, false], ledger.Calls.Select(c => c.Succeeded));
        Assert.All(ledger.Calls, c => Assert.Equal(4.5m, c.CostUsd));
    }

    [Fact]
    public async Task Run_KeepsTokensOfAnUnknownModel_Unpriced()
    {
        AddFileUnit("src/a.cs");
        var usage = new AgentUsage(10, 5, null, null, "mystery-1", null);

        await RunAsync(new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)) { Usage = usage });

        var call = Assert.Single(ledger.Calls);
        Assert.Equal((usage, (decimal?)null, (string?)null), (call.Usage, call.CostUsd, call.CostSource));
    }

    [Fact]
    public async Task Run_PricesFromConfig_AndALaterPriceChangeLeavesRecordedSpendUnchanged()
    {
        AddFileUnit("src/a.cs");
        AddFileUnit("src/b.cs");
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)) { Usage = new AgentUsage(1_000_000, 0, 0, 0, "in-house-7", null) };
        var cheap = Config.Default with { Prices = [new ModelPrice("in-house-7", 1, 1)] };
        var dear = Config.Default with { Prices = [new ModelPrice("in-house-7", 9, 9)] };
        await new Run(ledger, tree, clock, cheap with { BatchUnits = 1 }, adapter, new Progress<RunProgress>()).RunAsync(new RunOptions(1, 1, false, Path: "src/a.cs"), CancellationToken.None);

        await new Run(ledger, tree, clock, dear with { BatchUnits = 1 }, adapter, new Progress<RunProgress>()).RunAsync(new RunOptions(1, 1, false), CancellationToken.None);

        Assert.Equal([(1m, "config"), (9m, "config")], ledger.Calls.Select(c => (c.CostUsd!.Value, c.CostSource!)));
        var status = await new Status(ledger, dear).RunAsync(CancellationToken.None);
        Assert.Equal(10m, status.Spend!.CostUsd);
    }

    [Fact]
    public async Task VerifyRun_LabelsItsCallsWithTheVerifyVerb()
    {
        var unit = AddFileUnit("src/a.cs");
        var finding = new Finding("src/a.cs", 1, 1, Severity.High, "correctness", "claim", "evidence", 0.9, "default");
        ledger.Analyses.Add((new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "s", null), [finding]));
        ledger.Units[0] = unit with { Status = UnitStatus.Done };
        var verify = PlannedUnit.Verify((await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Single(), ledger.Members, Fidelity.Full);
        ledger.Units.Add(new Unit(verify.Id, verify.Kind, verify.Key, Fingerprints.Compute(verify.Members), UnitStatus.Pending, verify.Fidelity, null, null, null));
        ledger.Members.AddRange(verify.Members);
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult("""{"verdict": "confirmed", "reason": "it holds"}""")) { Usage = Sonnet };

        await new Run(ledger, tree, clock, Config.Default with { BatchUnits = 1 }, adapter, new Progress<RunProgress>()).RunAsync(new RunOptions(1, 1, false, UnitKind.Verify), CancellationToken.None);

        var call = Assert.Single(ledger.Calls);
        Assert.Equal((verify.Id, UnitKind.Verify, "verify " + At2(clock), true), (call.UnitId, call.Kind, call.Run, call.Succeeded));
    }

    [Fact]
    public async Task Fix_RecordsSuccessfulAndFailedWorkerCalls_WithTheirUsage()
    {
        var a = await SeedConfirmedAsync("a.cs");
        await SeedConfirmedAsync("b.cs");
        var editor = new Editor((path, _) => path == "a.cs"
            ? Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", a, [])), "a.cs") { Usage = Sonnet })
            : throw new InvalidOperationException("worker for b.cs failed", new AgentCallException("codex exited with code 1", Sonnet with { OutputTokens = 0 })));

        await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()), new FixOptions(1), null, CancellationToken.None);

        Assert.Equal([(UnitIds.Fix("a.cs"), true, 4.5m), (UnitIds.Fix("b.cs"), false, 3m)], ledger.Calls.Select(c => (c.UnitId!, c.Succeeded, c.CostUsd!.Value)));
        Assert.All(ledger.Calls, c => Assert.Equal((UnitKind.Fix, "fix " + At2(clock)), (c.Kind!.Value, c.Run)));
    }

    [Fact]
    public async Task Fix_RecordsARejectedRepair_AsAFailedCall()
    {
        await SeedConfirmedAsync("a.cs");
        var editor = new Editor((_, _) => Task.FromResult(new FileFixEdit("not json", "") { Usage = Sonnet }));

        await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()), new FixOptions(2), null, CancellationToken.None);

        Assert.Equal([false, false], ledger.Calls.Select(c => c.Succeeded));
    }

    [Fact]
    public async Task Fix_DoesNotRecordACall_WhenTheWorkerFailedBeforeTheAgentRan()
    {
        await SeedConfirmedAsync("a.cs");
        var editor = new Editor((_, _) => throw new InvalidOperationException("git worktree add failed"));

        await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()), new FixOptions(1), null, CancellationToken.None);

        Assert.Empty(ledger.Calls);
    }

    private static string At2(FakeClock clock) => Timestamps.Format(clock.UtcNow);

    private async Task<IReadOnlyList<long>> SeedConfirmedAsync(string path)
    {
        var unit = AddFileUnit(path);
        ledger.Units[ledger.Units.IndexOf(unit)] = unit with { Status = UnitStatus.Done };
        var finding = new Finding(path, 1, 1, Severity.High, "correctness", "claim " + path, "evidence", 0.9, "default");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "s", null), [finding], CancellationToken.None);
        var ids = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Where(f => f.UnitId == unit.Id).Select(f => f.Id).ToList();
        foreach (var id in ids) ledger.Verifications[id] = new VerifyResponse(Verdict.Confirmed, "it holds");
        return ids;
    }

    private sealed class Editor(Func<string, CancellationToken, Task<FileFixEdit>> run) : IFileFixer
    {
        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => run(path, cancellationToken);
    }
}
