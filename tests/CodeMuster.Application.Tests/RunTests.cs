using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class RunTests
{
    private const string EmptyResponse = """{"summary": "Nothing to report.", "findings": []}""";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly List<RunProgress> reports = [];
    private readonly ListProgress notes = new();

    private Unit AddFileUnit(string path, UnitStatus status = UnitStatus.Pending)
    {
        tree.Add(path, $"// {path}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), status, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        return unit;
    }

    private Unit AddOrphanUnit(string path, UnitStatus status = UnitStatus.Pending)
    {
        tree.Add(path, $"// {path}");
        var id = UnitIds.Orphan(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.Orphan, path, Fingerprints.Compute([member]), status, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        return unit;
    }

    private List<Unit> AddFileUnits(params string[] names) => names.Select(name => AddFileUnit($"src/{name}.cs")).ToList();

    private Task<RunResult> RunAsync(FakeAgentAdapter adapter, RunOptions options, CancellationToken cancellationToken = default, Action<RunProgress>? onReport = null) =>
        new Run(ledger, tree, clock, Config.Default, adapter, new RecordingProgress(reports, onReport), notes).RunAsync(options, cancellationToken);

    private static FakeAgentAdapter Always(string response) => new((_, _) => Task.FromResult(response));

    private static string UnitIdOf(string pack) =>
        pack.Split('\n').Single(line => line.StartsWith("- unit: ", StringComparison.Ordinal))["- unit: ".Length..];

    private Unit Stored(string unitId) => ledger.Units.Single(u => u.Id == unitId);

    private sealed class RecordingProgress(List<RunProgress> reports, Action<RunProgress>? onReport) : IProgress<RunProgress>
    {
        public void Report(RunProgress value)
        {
            reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    [Fact]
    public async Task Parallelism3_FiveUnits_HoldsThreeCallsOpenAtOnce_AndCompletesAll()
    {
        AddFileUnits("a", "b", "c", "d", "e");
        var adapter = Always(EmptyResponse);
        adapter.Gate = new TaskCompletionSource();

        var run = RunAsync(adapter, new RunOptions(3, 1, false));
        await adapter.WhenInFlightAsync(3);
        Assert.Equal(3, adapter.Packs.Count);
        Assert.Empty(ledger.Analyses);
        adapter.Gate.SetResult();
        var result = await run;

        Assert.Equal(5, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.False(result.Cancelled);
        Assert.Equal(3, adapter.MaxInFlight);
        Assert.Equal(5, adapter.Packs.Select(UnitIdOf).Distinct().Count());
        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Done, u.Status));
        Assert.Equal(5, ledger.Analyses.Count);
        Assert.Equal(5, reports.Count);
    }

    [Fact]
    public async Task Pack_SentToAdapter_IsTheHeadlessNextPack()
    {
        AddFileUnit("src/a.cs");
        var expected = Assert.Single(await new Next(ledger, tree, Config.Default, interactive: false).RunAsync(1, CancellationToken.None));
        var adapter = Always(EmptyResponse);

        await RunAsync(adapter, new RunOptions(1, 1, false));

        var sent = Assert.Single(adapter.Packs);
        Assert.Equal(expected.Markdown, sent);
        Assert.DoesNotContain("codemuster done", sent);
    }

    [Fact]
    public async Task InvalidJsonThreeTimes_WithMaxAttempts3_GivesUp_RecordsThreeFailures_AndOthersComplete()
    {
        var bad = AddFileUnit("src/a.cs");
        AddFileUnits("b", "c");
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(UnitIdOf(pack) == bad.Id ? "I could not analyze this unit." : EmptyResponse));
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await RunAsync(adapter, new RunOptions(2, 3, false), guard.Token);

        Assert.Equal(2, result.Completed);
        Assert.Equal([bad.Id], result.GaveUp);
        Assert.False(result.Cancelled);
        Assert.Equal(3, adapter.Packs.Count(pack => UnitIdOf(pack) == bad.Id));

        var failures = ledger.Analyses.Where(a => a.Analysis.UnitId == bad.Id).ToList();
        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.False(f.Analysis.Succeeded));
        Assert.Equal(UnitStatus.Failed, Stored(bad.Id).Status);
        Assert.Equal(UnitStatus.Done, Stored("file:src/b.cs").Status);
        Assert.Equal(UnitStatus.Done, Stored("file:src/c.cs").Status);

        var badReports = reports.Where(r => r.UnitId == bad.Id).ToList();
        Assert.Equal([1, 2, 3], badReports.Select(r => r.Attempt));
        Assert.All(badReports, r => Assert.Equal(DoneOutcome.InvalidResponse, r.Outcome));
        Assert.StartsWith("invalid response: ", badReports[0].Message);
        Assert.StartsWith("gave up after 3 attempt(s): invalid response: ", badReports[2].Message);
        Assert.Equal(5, reports.Count);
        Assert.All(reports, r => Assert.Equal(3, r.Total));
    }

    [Fact]
    public async Task FailsOnce_ThenSucceeds_CompletesOnSecondAttempt()
    {
        var unit = AddFileUnit("src/a.cs");
        var calls = 0;
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(++calls == 1 ? "not json" : EmptyResponse));

        var result = await RunAsync(adapter, new RunOptions(1, 3, false));

        Assert.Equal(1, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.Equal(UnitStatus.Done, Stored(unit.Id).Status);
        Assert.Equal([false, true], ledger.Analyses.Select(a => a.Analysis.Succeeded));
        Assert.Equal([(1, DoneOutcome.InvalidResponse, 0), (2, DoneOutcome.Recorded, 1)], reports.Select(r => (r.Attempt, r.Outcome, r.Completed)));
        Assert.All(reports, r => Assert.Equal(1, r.Total));
    }

    [Fact]
    public async Task AdapterException_CountsAsFailedAttempt_WithItsMessage_AndRecordsNothing()
    {
        var unit = AddFileUnit("src/a.cs");
        var calls = 0;
        var adapter = new FakeAgentAdapter((_, _) => ++calls == 1 ? throw new InvalidOperationException("claude exited with code 1") : Task.FromResult(EmptyResponse));

        var result = await RunAsync(adapter, new RunOptions(1, 2, false));

        Assert.Equal(1, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.Equal(new RunProgress(unit.Id, 1, DoneOutcome.Rejected, "claude exited with code 1", 0, 1), reports[0]);
        Assert.Equal(DoneOutcome.Recorded, reports[1].Outcome);
        Assert.True(Assert.Single(ledger.Analyses).Analysis.Succeeded);
        Assert.Equal(UnitStatus.Done, Stored(unit.Id).Status);
    }

    [Fact]
    public async Task AnyAdapterException_CountsAsAnAttempt_AndAGivenUpHeadUnitDoesNotBlockTheQueue()
    {
        var bad = AddFileUnit("src/a.cs");
        var good = AddFileUnit("src/b.cs");
        var adapter = new FakeAgentAdapter((pack, _) => UnitIdOf(pack) == bad.Id
            ? throw new System.Text.Json.JsonException("'o' is an invalid start of a value.")
            : Task.FromResult(EmptyResponse));
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await RunAsync(adapter, new RunOptions(1, 2, false), guard.Token);

        Assert.False(result.Cancelled);
        Assert.Equal([bad.Id], result.GaveUp);
        Assert.Equal(1, result.Completed);
        Assert.Equal(UnitStatus.Done, Stored(good.Id).Status);
        Assert.Equal(UnitStatus.Pending, Stored(bad.Id).Status);
        Assert.Equal("gave up after 2 attempt(s): 'o' is an invalid start of a value.", reports.Single(r => r.UnitId == bad.Id && r.Attempt == 2).Message);
    }

    [Fact]
    public async Task AdapterExceptionEveryTime_GivesUp_AndUnitStaysPending()
    {
        var unit = AddFileUnit("src/a.cs");
        var adapter = new FakeAgentAdapter((_, _) => throw new InvalidOperationException("boom"));
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await RunAsync(adapter, new RunOptions(1, 2, false), guard.Token);

        Assert.False(result.Cancelled);
        Assert.Equal(0, result.Completed);
        Assert.Equal([unit.Id], result.GaveUp);
        Assert.Equal(2, adapter.Packs.Count);
        Assert.Empty(ledger.Analyses);
        Assert.Equal(UnitStatus.Pending, Stored(unit.Id).Status);
        Assert.Equal("gave up after 2 attempt(s): boom", reports[1].Message);
    }

    [Fact]
    public async Task Cancel_WhileCallsAreHeldOpen_LeavesThemNotDone_AndSecondRunFinishesTheRest()
    {
        AddFileUnits("a", "b", "c", "d", "e");
        var adapter = Always(EmptyResponse);
        using var cts = new CancellationTokenSource();
        var secondBatch = new TaskCompletionSource();

        var run = RunAsync(adapter, new RunOptions(2, 1, false), cts.Token, report =>
        {
            if (report.Completed == 2)
            {
                adapter.Gate = new TaskCompletionSource();
                secondBatch.SetResult();
            }
        });
        await secondBatch.Task;
        await adapter.WhenInFlightAsync(2);
        cts.Cancel();
        var result = await run;

        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.Equal(2, reports.Count);
        Assert.Equal(2, ledger.Analyses.Count);
        Assert.Equal(4, adapter.Packs.Count);
        Assert.Equal(["file:src/a.cs", "file:src/b.cs"], ledger.Units.Where(u => u.Status == UnitStatus.Done).Select(u => u.Id));
        Assert.Equal(3, ledger.Units.Count(u => u.Status == UnitStatus.Pending));

        adapter.Gate = null;
        var second = await RunAsync(adapter, new RunOptions(2, 1, false));

        Assert.False(second.Cancelled);
        Assert.Equal(3, second.Completed);
        Assert.Equal(["file:src/c.cs", "file:src/d.cs", "file:src/e.cs"], adapter.Packs.Skip(4).Select(UnitIdOf).Order(StringComparer.Ordinal));
        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Done, u.Status));
        Assert.Equal(5, ledger.Analyses.Count);
        Assert.Equal([(1, 3), (2, 3), (3, 3)], reports.Skip(2).Select(r => (r.Completed, r.Total)));
    }

    [Fact]
    public async Task Cancel_WhenAdapterIgnoresTheToken_StillRecordsNothing()
    {
        AddFileUnit("src/a.cs");
        var gate = new TaskCompletionSource();
        var adapter = new FakeAgentAdapter(async (_, _) =>
        {
            await gate.Task;
            return EmptyResponse;
        });
        using var cts = new CancellationTokenSource();

        var run = RunAsync(adapter, new RunOptions(1, 1, false), cts.Token);
        await adapter.WhenInFlightAsync(1);
        cts.Cancel();
        gate.SetResult();
        var result = await run;

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Completed);
        Assert.Empty(ledger.Analyses);
        Assert.Empty(reports);
        Assert.Equal(UnitStatus.Pending, Stored("file:src/a.cs").Status);
    }

    [Fact]
    public async Task Force_ReanalyzesDoneUnits_AndLeavesRetiredAlone()
    {
        var a = AddFileUnit("src/a.cs");
        var b = AddFileUnit("src/b.cs");
        AddFileUnit("src/gone.cs", UnitStatus.Retired);
        var lensHash = Config.HashOf(Config.Default.Lenses);
        await ledger.RecordAnalysisAsync(new Analysis(a.Id, a.Fingerprint, lensHash, "2026-09-10T00:00:00.0000000Z", true, "A.", null), [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(b.Id, b.Fingerprint, lensHash, "2026-09-10T00:00:00.0000000Z", true, "B.", null), [], CancellationToken.None);
        var adapter = Always(EmptyResponse);

        var result = await RunAsync(adapter, new RunOptions(2, 1, true));

        Assert.Equal(2, result.Completed);
        Assert.Equal(["file:src/a.cs", "file:src/b.cs"], adapter.Packs.Select(UnitIdOf).Order(StringComparer.Ordinal));
        Assert.Equal(UnitStatus.Done, Stored(a.Id).Status);
        Assert.Equal(UnitStatus.Done, Stored(b.Id).Status);
        Assert.Equal(UnitStatus.Retired, Stored("file:src/gone.cs").Status);
        Assert.Equal(4, ledger.Analyses.Count);
        Assert.Equal("Nothing to report.", Stored(a.Id).Summary);
        Assert.Equal(3, ledger.Members.Count);
        Assert.All(reports, r => Assert.Equal(2, r.Total));
    }

    [Fact]
    public async Task WithoutForce_DoneUnitsAreLeftAlone()
    {
        var done = AddFileUnit("src/done.cs", UnitStatus.Done);
        AddFileUnit("src/a.cs");
        var adapter = Always(EmptyResponse);

        var result = await RunAsync(adapter, new RunOptions(4, 1, false));

        Assert.Equal(1, result.Completed);
        Assert.Equal("file:src/a.cs", UnitIdOf(Assert.Single(adapter.Packs)));
        Assert.Equal(done, Stored(done.Id));
        Assert.Equal(new RunProgress("file:src/a.cs", 1, DoneOutcome.Recorded, "recorded 0 finding(s) for file:src/a.cs", 1, 1), Assert.Single(reports));
    }

    [Fact]
    public async Task EachAttempt_SaysItStarted_BeforeItsResultIsKnown()
    {
        AddFileUnits("a", "b");

        await RunAsync(Always(EmptyResponse), new RunOptions(1, 1, false));

        Assert.Equal(["starting file:src/a.cs", "starting file:src/b.cs"], notes.Messages);
    }

    [Fact]
    public async Task ARetriedUnit_SaysItStartedAgain()
    {
        var unit = AddFileUnit("src/a.cs");
        var replies = 0;
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(replies++ == 0 ? "oops" : EmptyResponse));

        await RunAsync(adapter, new RunOptions(1, 2, false));

        Assert.Equal(["starting " + unit.Id, "starting " + unit.Id], notes.Messages);
    }

    [Fact]
    public async Task Progress_CountsCompletedAgainstTotalPendingWork()
    {
        AddFileUnit("src/done.cs", UnitStatus.Done);
        AddFileUnits("a", "b", "c");

        var result = await RunAsync(Always(EmptyResponse), new RunOptions(1, 1, false));

        Assert.Equal(3, result.Completed);
        Assert.Equal(["file:src/a.cs", "file:src/b.cs", "file:src/c.cs"], reports.Select(r => r.UnitId));
        Assert.Equal([(1, 3), (2, 3), (3, 3)], reports.Select(r => (r.Completed, r.Total)));
        Assert.All(reports, r => Assert.Equal(1, r.Attempt));
    }

    [Fact]
    public async Task FindingsRecordedDuringTheRun_AreVerifiedInTheSameRun_AndTheTotalGrowsToCoverThem()
    {
        AddFileUnit("src/a.cs");
        var finding = new Finding("src/a.cs", 1, 1, Severity.High, "security", "claim", "evidence", 0.9, "default");
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(pack.Contains("- kind: verify\n", StringComparison.Ordinal)
            ? VerifyResponseJson.Serialize(new VerifyResponse(Verdict.Confirmed, "Line 1 shows it."))
            : AnalysisResponseJson.Serialize(new AnalysisResponse("A", [finding]))));

        var result = await RunAsync(adapter, new RunOptions(1, 1, false));

        Assert.Equal(2, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.Equal([("file:src/a.cs", 1, 1), ("verify:1", 2, 2)], reports.Select(r => (r.UnitId, r.Completed, r.Total)));
        Assert.Equal(UnitStatus.Done, Stored(UnitIds.Verify(1)).Status);
        Assert.Equal(Verdict.Confirmed, ledger.Verifications[1].Verdict);
    }

    [Fact]
    public async Task Kind_WorksOnlyUnitsOfThatKind_AndCountsOnlyThem()
    {
        var file = AddFileUnit("src/a.cs");
        var orphan = AddOrphanUnit("src/b.cs");
        var adapter = Always(EmptyResponse);

        var result = await RunAsync(adapter, new RunOptions(4, 1, false, UnitKind.Orphan));

        Assert.Equal(1, result.Completed);
        Assert.Equal([orphan.Id], adapter.Packs.Select(UnitIdOf));
        Assert.Equal((1, 1), (reports.Single().Completed, reports.Single().Total));
        Assert.Equal(UnitStatus.Pending, Stored(file.Id).Status);
    }

    [Fact]
    public async Task ForceWithAKind_ReanalyzesOnlyThatKind()
    {
        var file = AddFileUnit("src/a.cs", UnitStatus.Done);
        var orphan = AddOrphanUnit("src/b.cs", UnitStatus.Done);
        var adapter = Always(EmptyResponse);

        var result = await RunAsync(adapter, new RunOptions(4, 1, true, UnitKind.Orphan));

        Assert.Equal(1, result.Completed);
        Assert.Equal([orphan.Id], adapter.Packs.Select(UnitIdOf));
        Assert.Equal(file, Stored(file.Id));
    }

    [Fact]
    public async Task FencedJsonOutput_IsAccepted()
    {
        var unit = AddFileUnit("src/a.cs");
        var response = AnalysisResponseJson.Sample.Replace("src/Billing/InvoiceRepository.cs", "src/a.cs", StringComparison.Ordinal);
        var adapter = Always("Here is my analysis.\n\n```json\n" + response + "\n```\n\nDone.");

        var result = await RunAsync(adapter, new RunOptions(1, 1, false));

        Assert.Equal(1, result.Completed);
        Assert.Equal(UnitStatus.Done, Stored(unit.Id).Status);
        var (analysis, findings) = Assert.Single(ledger.Analyses, a => a.Analysis.UnitId == unit.Id);
        Assert.True(analysis.Succeeded);
        Assert.Equal("src/a.cs", Assert.Single(findings).Path);
    }

    [Fact]
    public async Task NothingPending_ReturnsZero_WithoutCallingAdapter()
    {
        AddFileUnit("src/done.cs", UnitStatus.Done);
        var adapter = Always(EmptyResponse);

        var result = await RunAsync(adapter, new RunOptions(2, 2, false));

        Assert.Equal(0, result.Completed);
        Assert.Empty(result.GaveUp);
        Assert.False(result.Cancelled);
        Assert.Empty(adapter.Packs);
        Assert.Empty(reports);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 3)]
    public void RunOptions_RejectsNonPositiveParallelismOrAttempts(int parallelism, int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunOptions(parallelism, maxAttempts, false));
    }

    [Fact]
    public void RunOptions_AcceptsOneAndOne()
    {
        var options = new RunOptions(1, 1, true);

        Assert.Equal((1, 1, true), (options.Parallelism, options.MaxAttempts, options.Force));
    }
}
