using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class FixRunTests
{
    private const string At = "2026-09-12T00:00:00.0000000Z";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeWorkspace workspace = new();
    private readonly List<string> notes = [];
    private readonly FakeTestRunner tests = new();

    private async Task<IReadOnlyList<long>> SeedAsync(string path, params int[] lines)
    {
        tree.Add(path, $"// {path}\nclass A {{ }}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Done, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "content-" + path, 10, At, At, At, null, null, null, null, null, null);
        var findings = lines.Select(line => new Finding(path, line, line, Severity.High, "correctness", $"claim {line}", "evidence", 0.9, "default")).ToList();
        await ledger.RecordAnalysisAsync(new Analysis(id, unit.Fingerprint, "lens", At, true, "summary", null), findings, CancellationToken.None);
        var ids = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Where(f => f.UnitId == id).Select(f => f.Id).ToList();
        foreach (var finding in ids)
        {
            ledger.Verifications[finding] = new VerifyResponse(Verdict.Confirmed, "it holds");
        }

        return ids;
    }

    private Task<FixResult> RunAsync(FakeAgentAdapter adapter, int attempts = 2, ITestRunner? runner = null) =>
        new Fix(ledger, tree, clock, Config.Default, workspace, runner).RunAsync(
            adapter,
            new FixOptions(attempts),
            new ListProgress(notes),
            CancellationToken.None);

    private FakeAgentAdapter Fixer(Func<string, FixResponse> respond, bool touchesFiles = true) =>
        new((pack, _) =>
        {
            if (touchesFiles)
            {
                workspace.Clean = false;
            }

            return Task.FromResult(FixResponseJson.Serialize(respond(pack)));
        });

    [Fact]
    public async Task ADirtyTree_StopsBeforeAnythingRuns()
    {
        await SeedAsync("src/a.cs", 10);
        workspace.Clean = false;
        var adapter = Fixer(_ => new FixResponse("nope", [], []));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(adapter));

        Assert.Contains("working tree has uncommitted changes", error.Message);
        Assert.Empty(adapter.Packs);
        Assert.Empty(workspace.Commits);
    }

    [Fact]
    public async Task EachFixedFile_BecomesOneCommitNamingItsFindings()
    {
        var a = await SeedAsync("src/a.cs", 10, 20);
        var b = await SeedAsync("src/b.cs", 5);
        var adapter = Fixer(pack => pack.Contains("src/a.cs", StringComparison.Ordinal)
            ? new FixResponse("Scoped the query to the tenant.", a, [])
            : new FixResponse("Awaited the call.", b, []));

        var result = await RunAsync(adapter);

        Assert.Equal((2, 3, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Empty(result.GaveUp);
        Assert.Equal(2, workspace.Commits.Count);
        Assert.Contains("fix src/a.cs", workspace.Commits[0]);
        Assert.Contains("Scoped the query to the tenant.", workspace.Commits[0]);
        Assert.Contains($"Findings: {a[0]}, {a[1]}", workspace.Commits[0]);
        Assert.All(ledger.Units.Where(u => u.Kind == UnitKind.Fix), u => Assert.Equal(UnitStatus.Done, u.Status));
    }

    [Fact]
    public async Task AUnitThatOnlyDeclines_IsRecordedWithoutACommit()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Left alone.", [], [new DeclinedFix(ids[0], "The caller already guards this.")]), touchesFiles: false);

        var result = await RunAsync(adapter);

        Assert.Equal((1, 0, 1), (result.Units, result.Fixed, result.Declined));
        Assert.Empty(result.GaveUp);
        Assert.Empty(workspace.Commits);
        Assert.Equal(FixState.Declined, ledger.Fixes[ids[0]].State);
    }

    [Fact]
    public async Task AUnitThatKeepsFailing_IsGivenUpOn_AndItsChangesAreRestored()
    {
        await SeedAsync("src/a.cs", 10);
        var adapter = new FakeAgentAdapter((_, _) =>
        {
            workspace.Clean = false;
            return Task.FromResult("not json at all");
        });

        var result = await RunAsync(adapter);

        Assert.Equal(2, adapter.Packs.Count);
        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.Empty(workspace.Commits);
        Assert.Equal(2, workspace.Restores);
    }

    [Fact]
    public async Task EachUnit_SaysWhatItIsAbout_BeforeItRuns()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Done.", ids, []));

        await RunAsync(adapter);

        Assert.Equal(["fixing src/a.cs, 1 finding(s)"], notes);
    }

    [Fact]
    public async Task PassingTests_LetTheFixBeRecordedAndCommitted()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Fixed it.", ids, []));
        tests.Results.Enqueue(new TestRun(true, "all good"));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(1, tests.Runs);
        Assert.Equal((1, 1, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Single(workspace.Commits);
        Assert.Contains("running tests for src/a.cs", notes);
    }

    [Fact]
    public async Task FailingTests_ThrowTheFixAway_AndNothingIsRecorded()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Broke it.", ids, []));
        tests.Results.Enqueue(new TestRun(false, "MyTest failed: expected 2, got 3"));
        tests.Results.Enqueue(new TestRun(false, "MyTest failed: expected 2, got 3"));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(2, adapter.Packs.Count);
        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.Equal(0, result.Fixed);
        Assert.Empty(workspace.Commits);
        Assert.Equal(2, workspace.Restores);
        Assert.Empty(ledger.Fixes);
        Assert.Contains(notes, note => note.Contains("expected 2, got 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailingThenPassing_RecordsTheSecondAttempt()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Second time lucky.", ids, []));
        tests.Results.Enqueue(new TestRun(false, "broken"));
        tests.Results.Enqueue(new TestRun(true, ""));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal((1, 1, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Empty(result.GaveUp);
        Assert.Single(workspace.Commits);
        Assert.Equal(FixState.Fixed, ledger.Fixes[ids[0]].State);
    }

    [Fact]
    public async Task WithoutATestCommand_TheFixIsRecordedUnverified()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Fixed it.", ids, []));

        var result = await RunAsync(adapter);

        Assert.Equal(0, tests.Runs);
        Assert.False(result.TestsRun);
        Assert.Single(workspace.Commits);
    }

    [Fact]
    public async Task ADeclineOnlyUnit_StillRunsTheTests_SinceTheAgentMayHaveTouchedFiles()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Left alone.", [], [new DeclinedFix(ids[0], "Not real.")]), touchesFiles: false);
        tests.Results.Enqueue(new TestRun(true, ""));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(1, tests.Runs);
        Assert.True(result.TestsRun);
        Assert.Empty(workspace.Commits);
    }
}
