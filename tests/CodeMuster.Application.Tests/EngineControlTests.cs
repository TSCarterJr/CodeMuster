using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class EngineControlTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";
    private const string EmptyResponse = """{"summary": "Nothing to report.", "findings": []}""";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeWorkspace workspace = new();
    private readonly RecordingEvents events = new();
    private readonly FakeControl control = new();
    private readonly CancellationTokenSource guard = new(TimeSpan.FromSeconds(30));

    private List<Unit> AddFileUnits(params string[] names) => names.Select(name =>
    {
        var path = $"src/{name}.cs";
        tree.Add(path, $"// {path}\nclass A {{ }}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "content-" + path, 10, At, At, At, null, null, null, null, null, null);
        return unit;
    }).ToList();

    private static string UnitIdOf(string pack) =>
        pack.Split('\n').Single(line => line.StartsWith("- unit: ", StringComparison.Ordinal))["- unit: ".Length..];

    private static object? Field(EngineEvent e, string name) => e.Fields.TryGetValue(name, out var value) ? value : null;

    private Task<RunResult> RunAsync(IAgentAdapter adapter, int workers, Func<AgentIdentity, IAgentAdapter>? retarget = null) =>
        new Run(ledger, tree, clock, Config.Default, adapter, new Progress<RunProgress>(), null, events, control, retarget)
            .RunAsync(new RunOptions(workers, 1, false), guard.Token);

    // Each unit's call waits for its own release, so the test decides when every call ends.
    private sealed class Held
    {
        private readonly Dictionary<string, TaskCompletionSource> gates = [];

        public TaskCompletionSource For(string unitId)
        {
            lock (gates)
            {
                if (!gates.TryGetValue(unitId, out var gate)) gates[unitId] = gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return gate;
            }
        }

        public void Release(string unitId) => For(unitId).TrySetResult();

        public FakeAgentAdapter Adapter(AgentIdentity? identity = null) => new(async (pack, token) =>
        {
            await For(UnitIdOf(pack)).Task.WaitAsync(token);
            return EmptyResponse;
        })
        { Identity = identity ?? new AgentIdentity("fake") };
    }

    private async Task WaitForAsync(Func<bool> condition)
    {
        while (!condition()) await Task.Delay(1, guard.Token);
    }

    private Task AppliedAsync(string command, int count = 1) =>
        WaitForAsync(() => events.OfType("command_applied").Count(e => Equals(Field(e, "command"), command)) >= count);

    [Fact]
    public async Task Pause_StopsStartingUnits_LetsTheRunningOneFinish_AndResumeCarriesOn()
    {
        var units = AddFileUnits("a", "b", "c");
        var held = new Held();
        var adapter = held.Adapter();

        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);
        control.Send("pause");
        await AppliedAsync("pause");
        held.Release(units[0].Id);
        await WaitForAsync(() => events.OfType("unit_finished").Count == 1);
        await Task.Delay(100, guard.Token);

        Assert.Single(adapter.Packs);
        Assert.False(run.IsCompleted);
        Assert.Single(events.OfType("paused"));
        held.Release(units[1].Id);
        held.Release(units[2].Id);
        control.Send("resume");
        var result = await run;

        Assert.Equal(3, result.Completed);
        Assert.Single(events.OfType("resumed"));
        Assert.Equal("run_summary", events.Events[^1].Type);
    }

    [Fact]
    public async Task Stop_CancelsTheRunningCall_LikeCtrlC_AndRecordsNothingForIt()
    {
        var units = AddFileUnits("a", "b");
        var held = new Held();
        var adapter = held.Adapter();

        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);
        control.Send("stop");
        var result = await run;

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Completed);
        Assert.Empty(ledger.Analyses);
        Assert.All(units, u => Assert.Equal(UnitStatus.Pending, ledger.Units.Single(s => s.Id == u.Id).Status));
        Assert.Equal(["command_applied", "stopped", "run_summary"], events.Events.SkipWhile(e => e.Type != "command_applied").Select(e => e.Type));
        Assert.Equal(true, Field(events.Events[^1], "cancelled"));
    }

    [Fact]
    public async Task Workers_GrowsThePoolWhileRunning()
    {
        var units = AddFileUnits("a", "b", "c", "d");
        var held = new Held();
        var adapter = held.Adapter();

        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);
        control.Send("workers 3");
        await adapter.WhenInFlightAsync(3).WaitAsync(guard.Token);
        foreach (var unit in units) held.Release(unit.Id);
        var result = await run;

        Assert.Equal(4, result.Completed);
        Assert.Equal(3, adapter.MaxInFlight);
        var applied = Assert.Single(events.OfType("command_applied"));
        Assert.Equal(("workers", 3, "workers 3"), (Field(applied, "command"), Field(applied, "value"), Field(applied, "line")));
    }

    [Fact]
    public async Task ModelAndEffort_ApplyToUnitsNotYetStarted_AndEachAnalysisRecordsWhatItRanWith()
    {
        var units = AddFileUnits("a", "b");
        var held = new Held();
        var original = held.Adapter(new AgentIdentity("claude", "sonnet", "low"));
        var retargeted = new List<AgentIdentity>();
        var run = RunAsync(original, 1, identity =>
        {
            retargeted.Add(identity);
            return held.Adapter(identity);
        });
        await original.WhenInFlightAsync(1).WaitAsync(guard.Token);

        control.Send("model opus");
        control.Send("""{"command": "effort", "value": "max"}""");
        await AppliedAsync("effort");
        held.Release(units[0].Id);
        held.Release(units[1].Id);
        var result = await run;

        Assert.Equal(2, result.Completed);
        Assert.Equal([new AgentIdentity("claude", "opus", "max")], retargeted);
        Assert.Equal([new AgentIdentity("claude", "sonnet", "low"), new AgentIdentity("claude", "opus", "max")],
            ledger.Analyses.OrderBy(a => a.Analysis.UnitId, StringComparer.Ordinal).Select(a => a.Analysis.By));
        Assert.Equal(["model", "effort"], events.OfType("command_applied").Select(e => Field(e, "command")));
    }

    [Fact]
    public async Task ModelOrEffort_IsRejected_WhenTheRunCannotChangeTheAgent()
    {
        AddFileUnits("a");
        var held = new Held();
        var adapter = held.Adapter();
        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);

        control.Send("model opus");
        await WaitForAsync(() => events.OfType("command_rejected").Count == 1);
        held.Release(UnitIds.File("src/a.cs"));
        await run;

        Assert.Contains("cannot change", (string)Field(events.OfType("command_rejected")[0], "reason")!);
    }

    [Theory]
    [InlineData("workers 0", "workers needs a whole number of at least 1")]
    [InlineData("workers two", "workers needs a whole number of at least 1")]
    [InlineData("resume", "not paused")]
    [InlineData("dance", "unknown command 'dance'")]
    [InlineData("{\"command\": ", "not valid JSON")]
    [InlineData("model", "model needs a value")]
    [InlineData("effort   ", "effort needs a value")]
    public async Task ABadCommand_IsRejectedWithAReason_AndTheRunCarriesOn(string line, string reason)
    {
        var units = AddFileUnits("a");
        var held = new Held();
        var adapter = held.Adapter();
        var run = RunAsync(adapter, 1, identity => held.Adapter(identity));
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);

        control.Send(line);
        await WaitForAsync(() => events.OfType("command_rejected").Count == 1);
        held.Release(units[0].Id);
        var result = await run;

        Assert.Equal(1, result.Completed);
        var rejected = events.OfType("command_rejected")[0];
        Assert.Equal(line, Field(rejected, "line"));
        Assert.Contains(reason, (string)Field(rejected, "reason")!);
        Assert.Empty(events.OfType("command_applied"));
    }

    [Fact]
    public async Task PauseTwice_TheSecondIsRejected_AndBlankLinesAreIgnored()
    {
        var units = AddFileUnits("a");
        var held = new Held();
        var adapter = held.Adapter();
        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);

        control.Send("PAUSE");
        control.Send("  ");
        control.Send("{\"command\": \"pause\"}");
        control.Send("resume");
        await AppliedAsync("resume");
        held.Release(units[0].Id);
        await run;

        Assert.Equal(["pause", "resume"], events.OfType("command_applied").Select(e => Field(e, "command")));
        Assert.Equal("already paused", Field(Assert.Single(events.OfType("command_rejected")), "reason"));
    }

    [Fact]
    public async Task APausedRun_WithNothingLeftToDo_Finishes()
    {
        var units = AddFileUnits("a");
        var held = new Held();
        var adapter = held.Adapter();
        var run = RunAsync(adapter, 1);
        await adapter.WhenInFlightAsync(1).WaitAsync(guard.Token);

        control.Send("pause");
        await AppliedAsync("pause");
        held.Release(units[0].Id);
        var result = await run;

        Assert.Equal(1, result.Completed);
    }

    [Fact]
    public async Task Fix_PausesResizesRetargetsAndStops()
    {
        await SeedConfirmedAsync("a");
        await SeedConfirmedAsync("b");
        await SeedConfirmedAsync("c");
        var gates = new Held();
        var identities = new List<(string Path, AgentIdentity Identity)>();
        var editor = new IdentityEditor(async (path, identity, token) =>
        {
            lock (identities) identities.Add((path, identity));
            await gates.For(path).Task.WaitAsync(token);
            return new FileFixEdit("not json", "");
        });
        var fix = new Fix(ledger, tree, clock, Config.Default, workspace, null, editor, events: events, control: control)
            .RunAsync(new FakeAgentAdapter((_, _) => throw new InvalidOperationException()) { Identity = new AgentIdentity("codex", "gpt", "low") }, new FixOptions(1), null, guard.Token);
        await WaitForAsync(() => identities.Count == 1);

        control.Send("pause");
        control.Send("workers 2");
        control.Send("model gpt-max");
        await AppliedAsync("model");
        gates.Release("src/a.cs");
        await WaitForAsync(() => events.OfType("unit_finished").Count == 1);
        await Task.Delay(100, guard.Token);
        Assert.Single(identities);
        control.Send("resume");
        await WaitForAsync(() => identities.Count == 3);
        control.Send("stop");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fix);
        Assert.Equal([("src/a.cs", new AgentIdentity("codex", "gpt", "low")), ("src/b.cs", new AgentIdentity("codex", "gpt-max", "low")), ("src/c.cs", new AgentIdentity("codex", "gpt-max", "low"))],
            identities.OrderBy(i => i.Path, StringComparer.Ordinal));
        Assert.Equal(["pause", "workers", "model", "resume", "stop"], events.OfType("command_applied").Select(e => Field(e, "command")));
        Assert.Equal("run_summary", events.Events[^1].Type);
        Assert.Equal(true, Field(events.Events[^1], "cancelled"));
        Assert.Equal(new AgentIdentity("codex", "gpt", "low"), ledger.Analyses.Single(a => a.Analysis.UnitId == UnitIds.Fix("src/a.cs") && !a.Analysis.Succeeded).Analysis.By);
    }

    private async Task SeedConfirmedAsync(string name)
    {
        var unit = AddFileUnits(name)[0] with { Status = UnitStatus.Done };
        ledger.Units[^1] = unit;
        var finding = new Finding(unit.Key, 1, 1, Severity.High, "correctness", "claim " + name, "evidence", 0.9, "default");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "s", null), [finding], CancellationToken.None);
        foreach (var id in (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Where(f => f.UnitId == unit.Id).Select(f => f.Id))
            ledger.Verifications[id] = new VerifyResponse(Verdict.Confirmed, "it holds");
    }

    private sealed class IdentityEditor(Func<string, AgentIdentity, CancellationToken, Task<FileFixEdit>> run) : IFileFixer
    {
        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => throw new InvalidOperationException("fix names the identity");

        public Task<FileFixEdit> RunAsync(string path, string pack, AgentIdentity identity, CancellationToken cancellationToken) => run(path, identity, cancellationToken);
    }
}
