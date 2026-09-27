using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class EngineEventTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";
    private const string EmptyResponse = """{"summary": "Nothing to report.", "findings": []}""";
    private static readonly AgentUsage Paid = new(100, 20, 3, 4, "sonnet-answered", 0.25m);

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeWorkspace workspace = new();
    private readonly RecordingEvents events = new();

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

    private static string UnitIdOf(string pack) =>
        pack.Split('\n').Single(line => line.StartsWith("- unit: ", StringComparison.Ordinal))["- unit: ".Length..];

    private Task<RunResult> RunAsync(IAgentAdapter adapter, RunOptions options, CancellationToken cancellationToken = default) =>
        new Run(ledger, tree, clock, Config.Default, adapter, new Progress<RunProgress>(), null, events).RunAsync(options, cancellationToken);

    private static object? Field(EngineEvent e, string name) => e.Fields.TryGetValue(name, out var value) ? value : null;

    [Fact]
    public async Task Run_PairsEveryStartWithAFinish_ReportsFindingsAndVerdicts_AndEndsWithTheSummary()
    {
        var a = AddFileUnit("src/a.cs");
        var b = AddFileUnit("src/b.cs");
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(pack.Contains("- kind: verify\n", StringComparison.Ordinal)
            ? VerifyResponseJson.Serialize(new VerifyResponse(Verdict.Confirmed, "Line 1 shows it."))
            : AnalysisResponseJson.Serialize(new AnalysisResponse("A", [new Finding(UnitIdOf(pack)["file:".Length..], 1, 1, Severity.High, "security", "claim", "evidence", 0.9, "default")]))))
        {
            Usage = Paid,
            Identity = new AgentIdentity("claude", "sonnet", "high"),
        };

        var result = await RunAsync(adapter, new RunOptions(2, 1, false));

        Assert.Equal(4, result.Completed);
        var all = events.Events;
        Assert.Equal("run_started", all[0].Type);
        Assert.Equal(("run", 2, "claude", "sonnet", "high"), (Field(all[0], "command"), Field(all[0], "workers"), Field(all[0], "agent"), Field(all[0], "model"), Field(all[0], "effort")));
        var summary = all[^1];
        Assert.Equal("run_summary", summary.Type);
        Assert.Equal(("run", 4, 0, 0, false), (Field(summary, "command"), Field(summary, "completed"), Field(summary, "gave_up"), Field(summary, "skipped"), Field(summary, "cancelled")));

        var started = events.OfType("unit_started");
        var finished = events.OfType("unit_finished");
        Assert.Equal(4, started.Count);
        Assert.Equal(started.Select(e => Field(e, "unit")).Order(), finished.Select(e => Field(e, "unit")).Order());
        Assert.All(started, e =>
        {
            Assert.Contains(Field(e, "worker"), new object?[] { 1, 2 });
            Assert.Equal(1, Field(e, "attempt"));
            Assert.True(all.ToList().IndexOf(e) < all.ToList().FindIndex(f => f.Type == "unit_finished" && Equals(Field(f, "unit"), Field(e, "unit"))));
        });
        Assert.Equal(["file", "file", "verify", "verify"], started.Select(e => (string)Field(e, "kind")!));

        var first = finished.Single(e => Equals(Field(e, "unit"), a.Id));
        Assert.Equal(("recorded", 1, 0L, 0.25m, "harness"), (Field(first, "outcome"), Field(first, "attempt"), Field(first, "duration_ms"), Field(first, "cost_usd"), Field(first, "cost_source")));
        var usage = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Field(first, "usage"));
        Assert.Equal((100L, 20L, 3L, 4L, "sonnet-answered", 0.25m), ((long?)usage["input_tokens"], (long?)usage["output_tokens"], (long?)usage["cache_read_tokens"], (long?)usage["cache_write_tokens"], usage["model"], (decimal?)usage["reported_cost_usd"]));

        var findings = events.OfType("finding_recorded");
        Assert.Equal([a.Id, b.Id], findings.Select(e => (string)Field(e, "unit")!).Order(StringComparer.Ordinal));
        Assert.All(findings, e => Assert.Equal(("high", "security", 1), (Field(e, "severity"), Field(e, "category"), Field(e, "line"))));
        var verdicts = events.OfType("verify_outcome");
        Assert.Equal(2, verdicts.Count);
        Assert.All(verdicts, e => Assert.Equal("confirmed", Field(e, "verdict")));
    }

    [Fact]
    public async Task Run_ARetriedUnit_FinishesWithItsOutcome_AndStartsAgainAsTheNextAttempt()
    {
        var unit = AddFileUnit("src/a.cs");
        var replies = 0;
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(replies++ == 0 ? "not json" : EmptyResponse));

        await RunAsync(adapter, new RunOptions(1, 2, false));

        Assert.Equal([("unit_started", 1), ("unit_finished", 1), ("unit_started", 2), ("unit_finished", 2)],
            events.Events.Where(e => e.Type.StartsWith("unit_", StringComparison.Ordinal)).Select(e => (e.Type, (int)Field(e, "attempt")!)));
        Assert.Equal(["invalid_response", "recorded"], events.OfType("unit_finished").Select(e => Field(e, "outcome")));
        Assert.All(events.OfType("unit_finished"), e => Assert.Null(Field(e, "cost_usd")));
        Assert.Equal(unit.Id, Field(events.OfType("unit_started")[0], "unit"));
    }

    [Fact]
    public async Task Run_AGivenUpUnit_IsMarkedOnItsLastFinish()
    {
        AddFileUnit("src/a.cs");

        var result = await RunAsync(new FakeAgentAdapter((_, _) => Task.FromResult("not json")), new RunOptions(1, 2, false));

        Assert.Single(result.GaveUp);
        Assert.Equal([false, true], events.OfType("unit_finished").Select(e => Field(e, "gave_up")));
        Assert.Equal(1, Field(events.Events[^1], "gave_up"));
    }

    [Fact]
    public async Task Run_EmitsSkipped_ForAnOversizedPack_WithoutAStart()
    {
        var unit = AddFileUnit("src/a.cs");
        tree.Contents[unit.Key] = new string('x', Config.Default.SliceTokenBudget * 4 + 1);

        await RunAsync(new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)), new RunOptions(1, 1, false));

        var skipped = Assert.Single(events.OfType("skipped"));
        Assert.Equal(unit.Id, Field(skipped, "unit"));
        Assert.Contains("slice_token_budget", (string)Field(skipped, "reason")!);
        Assert.Empty(events.OfType("unit_started"));
        Assert.Equal(1, Field(events.Events[^1], "skipped"));
    }

    [Fact]
    public async Task Run_WhenCancelled_StillEndsWithTheSummary()
    {
        AddFileUnit("src/a.cs");
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)) { Gate = new TaskCompletionSource() };
        using var cts = new CancellationTokenSource();

        var run = RunAsync(adapter, new RunOptions(1, 1, false), cts.Token);
        await adapter.WhenInFlightAsync(1);
        await cts.CancelAsync();
        var result = await run;

        Assert.True(result.Cancelled);
        Assert.Equal("run_summary", events.Events[^1].Type);
        Assert.Equal(true, Field(events.Events[^1], "cancelled"));
        Assert.Single(events.OfType("unit_started"));
        Assert.Empty(events.OfType("unit_finished"));
    }

    [Fact]
    public async Task Verify_NamesItsCommand()
    {
        await RunAsync(new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)), new RunOptions(1, 1, false, UnitKind.Verify));

        Assert.Equal(["run_started", "run_summary"], events.Events.Select(e => e.Type));
        Assert.All(events.Events, e => Assert.Equal("verify", Field(e, "command")));
    }

    [Fact]
    public async Task Fix_PairsStartsAndFinishes_ReportsTheOutcome_AndEndsWithTheSummary()
    {
        var ids = await SeedConfirmedAsync("a.cs");
        var editor = new Editor((_, _) => Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids, [])), "a.cs") { Usage = Paid }));

        await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor, events: events)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()) { Identity = new AgentIdentity("codex", "gpt", "low") }, new FixOptions(1), null, CancellationToken.None);

        Assert.Equal(["run_started", "unit_started", "unit_finished", "fix_outcome", "run_summary"], events.Events.Select(e => e.Type));
        var all = events.Events;
        Assert.Equal(("fix", 1, "codex", "gpt", "low"), (Field(all[0], "command"), Field(all[0], "workers"), Field(all[0], "agent"), Field(all[0], "model"), Field(all[0], "effort")));
        Assert.Equal((UnitIds.Fix("a.cs"), "fix", "a.cs", 1, 1), (Field(all[1], "unit"), Field(all[1], "kind"), Field(all[1], "key"), Field(all[1], "worker"), Field(all[1], "attempt")));
        Assert.Equal(("recorded", 0.25m), (Field(all[2], "outcome"), Field(all[2], "cost_usd")));
        Assert.Equal(ids, Assert.IsAssignableFrom<IEnumerable<long>>(Field(all[3], "fixed")));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<long>>(Field(all[3], "declined")));
        Assert.Equal(("fix", 1, 1, 0, 0, false), (Field(all[4], "command"), Field(all[4], "units"), Field(all[4], "fixed"), Field(all[4], "declined"), Field(all[4], "gave_up"), Field(all[4], "cancelled")));
    }

    [Fact]
    public async Task Fix_ARejectedRepair_FinishesRejected()
    {
        await SeedConfirmedAsync("a.cs");
        var editor = new Editor((_, _) => Task.FromResult(new FileFixEdit("not json", "") { Usage = Paid }));

        await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor, events: events)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()), new FixOptions(1), null, CancellationToken.None);

        Assert.Equal(["rejected"], events.OfType("unit_finished").Select(e => Field(e, "outcome")));
        Assert.Equal("run_summary", events.Events[^1].Type);
        Assert.Equal(1, Field(events.Events[^1], "gave_up"));
    }

    [Fact]
    public async Task Scan_ReportsEachStep_AndEndsWithWhatItPlanned()
    {
        tree.Add("src/a.cs", "class A { }");
        tree.Add("docs/readme.md", "# hi");

        var result = await new Scan(ledger, tree, new FakeContentHasher(), clock, Config.Default, events: events).RunAsync(CancellationToken.None);

        var steps = events.OfType("scan_progress").Select(e => (string)Field(e, "message")!).ToList();
        Assert.Contains("planning units", steps);
        Assert.Contains(steps, s => s.StartsWith("listed 2 files", StringComparison.Ordinal));
        var summary = events.Events[^1];
        Assert.Equal("scan_summary", summary.Type);
        Assert.Equal((result.HeadCommit, result.FilesIncluded, result.FilesExcluded, result.UnitsCreated, result.UnitsStale, result.UnitsTotal),
            (Field(summary, "head"), Field(summary, "files_included"), Field(summary, "files_excluded"), Field(summary, "units_created"), Field(summary, "units_stale"), Field(summary, "units_total")));
    }

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
