using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

/// <summary>
/// Pool tests step a run through many continuations, and xunit runs every async test's continuations on a few shared threads,
/// so they run on their own after the parallel tests: on a busy CI runner the shared threads once held them past a 20 s guard.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RunPool
{
    public const string Name = "run pool";
}

[Collection(RunPool.Name)]
public class RunPoolTests
{
    // Only a backstop against a hang; the tests step virtual time and never wait on the clock.
    private static readonly TimeSpan Guard = TimeSpan.FromMinutes(1);

    private const string EmptyResponse = """{"summary": "Nothing to report.", "findings": []}""";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly List<RunProgress> reports = [];
    private readonly VirtualTime time = new();

    private List<Unit> AddFileUnits(params string[] names) => names.Select(name =>
    {
        var path = $"src/{name}.cs";
        tree.Add(path, $"// {path}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        return unit;
    }).ToList();

    private static string UnitIdOf(string pack) =>
        pack.Split('\n').Single(line => line.StartsWith("- unit: ", StringComparison.Ordinal))["- unit: ".Length..];

    private Run Running(FakeAgentAdapter adapter) =>
        new(ledger, tree, clock, Config.Default with { BatchUnits = 1 }, adapter, new Recording(reports));

    // Each call sleeps for its unit's ticks of virtual time, so the test decides when every call ends.
    private FakeAgentAdapter Sleeping(Dictionary<string, int> ticks) => new(async (pack, _) =>
    {
        await time.Sleep(ticks[UnitIdOf(pack)]);
        return EmptyResponse;
    });

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    // Advances virtual time one tick at a time, each time once the run has refilled every slot it should.
    private async Task<int> DriveAsync(Task run, Func<int> expectedInFlight, CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitUntilAsync(() => run.IsCompleted || time.Waiting == expectedInFlight(), cancellationToken);
            if (run.IsCompleted) return time.Now;
            time.Tick();
        }
    }

    [Fact]
    public async Task ASlowUnit_DoesNotHoldBackTheOtherSlot()
    {
        var units = AddFileUnits("a", "b", "c", "d");
        var slow = new TaskCompletionSource();
        var adapter = new FakeAgentAdapter(async (pack, _) =>
        {
            if (UnitIdOf(pack) == units[0].Id) await slow.Task;
            return EmptyResponse;
        });
        using var guard = new CancellationTokenSource(Guard);

        var run = Running(adapter).RunAsync(new RunOptions(2, 1, false), guard.Token);
        await WaitUntilAsync(() => reports.Count == 3, guard.Token);

        Assert.Equal(["file:src/b.cs", "file:src/c.cs", "file:src/d.cs"], reports.Select(r => r.UnitId));
        Assert.All(units.Skip(1), u => Assert.Equal(UnitStatus.Done, ledger.Units.Single(s => s.Id == u.Id).Status));
        Assert.False(run.IsCompleted);
        slow.SetResult();
        var result = await run;

        Assert.Equal(4, result.Completed);
        Assert.Equal(units[0].Id, reports[^1].UnitId);
    }

    [Fact]
    public async Task TwoWorkers_WithCallsOfFiveOneOneAndOneTicks_FinishInFiveTicks()
    {
        var units = AddFileUnits("a", "b", "c", "d");
        var ticks = new Dictionary<string, int> { [units[0].Id] = 5, [units[1].Id] = 1, [units[2].Id] = 1, [units[3].Id] = 1 };
        using var guard = new CancellationTokenSource(Guard);

        var run = Running(Sleeping(ticks)).RunAsync(new RunOptions(2, 1, false), guard.Token);
        var finished = await DriveAsync(run, () => Math.Min(2, 4 - reports.Count), guard.Token);

        Assert.Equal(5, finished);
        Assert.Equal(4, (await run).Completed);
    }

    [Fact]
    public async Task CallsThatEndTogether_AreRecordedInTheOrderTheyStarted()
    {
        var units = AddFileUnits("a", "b", "c", "d", "e");

        var result = await Running(new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse))).RunAsync(new RunOptions(2, 1, false), CancellationToken.None);

        Assert.Equal(5, result.Completed);
        Assert.Equal(units.Select(u => u.Id), reports.Select(r => r.UnitId));
    }

    [Fact]
    public async Task UnevenCalls_CompleteEveryUnitExactlyOnce()
    {
        var names = Enumerable.Range(0, 10).Select(i => "u" + i).ToArray();
        var units = AddFileUnits(names);
        int[] durations = [3, 1, 2, 1, 1, 4, 1, 2, 1, 1];
        var ticks = units.Select((u, i) => (u.Id, durations[i])).ToDictionary(p => p.Id, p => p.Item2);
        var adapter = Sleeping(ticks);
        using var guard = new CancellationTokenSource(Guard);

        var run = Running(adapter).RunAsync(new RunOptions(3, 1, false), guard.Token);
        await DriveAsync(run, () => Math.Min(3, 10 - reports.Count), guard.Token);
        var result = await run;

        Assert.Equal(10, result.Completed);
        Assert.Equal(units.Select(u => u.Id).Order(StringComparer.Ordinal), adapter.Packs.Select(UnitIdOf).Order(StringComparer.Ordinal));
        Assert.Equal(10, reports.Select(r => r.UnitId).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 10), reports.Select(r => r.Completed));
        Assert.Equal(10, ledger.Analyses.Count);
        Assert.Equal(3, adapter.MaxInFlight);
    }

    [Fact]
    public async Task GrowingThePool_StartsMoreUnitsAtOnce_WithoutWaitingForASlotToFree()
    {
        var units = AddFileUnits("a", "b", "c", "d");
        var adapter = Sleeping(units.ToDictionary(u => u.Id, _ => 1));
        using var guard = new CancellationTokenSource(Guard);
        var runner = Running(adapter);

        var run = runner.RunAsync(new RunOptions(1, 1, false), guard.Token);
        await WaitUntilAsync(() => time.Waiting == 1, guard.Token);
        runner.Resize(3);
        await WaitUntilAsync(() => time.Waiting == 3, guard.Token);
        Assert.Empty(reports);
        await DriveAsync(run, () => Math.Min(3, 4 - reports.Count), guard.Token);

        Assert.Equal(4, (await run).Completed);
        Assert.Equal(3, adapter.MaxInFlight);
    }

    [Fact]
    public async Task ShrinkingThePool_LetsRunningUnitsFinish_AndThenStartsOneAtATime()
    {
        var units = AddFileUnits("a", "b", "c", "d", "e", "f");
        var adapter = Sleeping(units.ToDictionary(u => u.Id, _ => 1));
        using var guard = new CancellationTokenSource(Guard);
        var runner = Running(adapter);

        var run = runner.RunAsync(new RunOptions(3, 1, false), guard.Token);
        await WaitUntilAsync(() => time.Waiting == 3, guard.Token);
        runner.Resize(1);
        time.Tick();
        var inFlight = new List<int>();
        await DriveAsync(run, () =>
        {
            inFlight.Add(time.Waiting);
            return Math.Min(1, 6 - reports.Count);
        }, guard.Token);

        Assert.Equal(6, (await run).Completed);
        Assert.Equal(4, time.Now);
        Assert.Equal(1, inFlight.Max());
        Assert.Equal(3, adapter.MaxInFlight);
    }

    [Fact]
    public void Resize_RejectsFewerThanOneWorker()
    {
        var runner = Running(new FakeAgentAdapter((_, _) => Task.FromResult(EmptyResponse)));

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Resize(0));
    }

    private sealed class Recording(List<RunProgress> reports) : IProgress<RunProgress>
    {
        public void Report(RunProgress value)
        {
            lock (reports) reports.Add(value);
        }
    }

    private sealed class VirtualTime
    {
        private readonly List<(int Due, TaskCompletionSource Done)> sleepers = [];
        private int now;

        public int Now => Volatile.Read(ref now);

        public int Waiting
        {
            get
            {
                lock (sleepers) return sleepers.Count;
            }
        }

        public Task Sleep(int ticks)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sleepers) sleepers.Add((now + ticks, done));
            return done.Task;
        }

        public void Tick()
        {
            List<TaskCompletionSource> due;
            lock (sleepers)
            {
                now++;
                due = sleepers.Where(s => s.Due <= now).Select(s => s.Done).ToList();
                sleepers.RemoveAll(s => s.Due <= now);
            }

            foreach (var done in due) done.SetResult();
        }
    }
}
