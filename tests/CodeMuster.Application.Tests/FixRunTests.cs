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

    [Fact]
    public async Task LedgerFailureAfterCommitPreservesTheCommitAndLeavesFindingUnfixed()
    {
        var ids = await SeedAsync("a.cs", 10);
        ledger.OnRecordFix = () => throw new InvalidOperationException("disk full");
        var editor = new PackFixer(_ => new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids, [])), "a.cs"));
        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor)
            .RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), new ListProgress(notes), CancellationToken.None);
        Assert.Equal([UnitIds.Fix("a.cs")], result.GaveUp);
        Assert.Contains(notes, note => note.Contains("preserved", StringComparison.Ordinal) && note.Contains("disk full", StringComparison.Ordinal));
        Assert.Single(workspace.Commits);
        Assert.Empty(workspace.RestoredFiles);
        Assert.Empty(ledger.Fixes);
        Assert.Contains(ledger.Analyses, a => !a.Analysis.Succeeded && a.Analysis.Error!.Contains("disk full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitingPacksAreNotReadUntilAWorkerSlotIsAvailable()
    {
        var a = await SeedAsync("a.cs", 10);
        var b = await SeedAsync("b.cs", 10);
        tree.Contents.Remove("b.cs");
        var editor = new FileFixer((path, _) =>
        {
            if (path == "a.cs") tree.Contents["b.cs"] = "class B {}";
            return Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", path == "a.cs" ? a : b, [])), path));
        });
        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor)
            .RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), null, CancellationToken.None);
        Assert.Equal(2, result.Fixed);
    }

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
        CreateFix(adapter, runner).RunAsync(
            adapter,
            new FixOptions(attempts),
            new ListProgress(notes),
            CancellationToken.None);

    private Fix CreateFix(FakeAgentAdapter adapter, ITestRunner? runner = null) =>
        new(ledger, tree, clock, Config.Default, workspace, runner, new AdapterFixer(adapter, workspace));

    private sealed class AdapterFixer(IAgentAdapter adapter, FakeWorkspace workspace) : IFileFixer
    {
        public async Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
        {
            try
            {
                var response = await adapter.RunAsync(pack, cancellationToken);
                return new FileFixEdit(response, workspace.Clean ? "" : path);
            }
            finally
            {
                workspace.Clean = true;
            }
        }
    }

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
    public async Task RetryDeclined_LeavesFixedFindingsAlone_AndIncludesTheDeclineReason()
    {
        var ids = await SeedAsync("a.cs", 10, 20);
        await RunAsync(Fixer(_ => new FixResponse("partly fixed", [ids[0]], [new DeclinedFix(ids[1], "needs precise formatting in caller")])));
        string? pack = null;
        var retry = Fixer(text => { pack = text; return new FixResponse("caller fixed", [ids[1]], []); });
        var result = await CreateFix(retry).RunAsync(retry, new FixOptions(RetryDeclined: true), null, CancellationToken.None);
        Assert.Equal(1, result.Fixed);
        Assert.Contains("needs precise formatting in caller", pack);
        Assert.DoesNotContain("claim 10", pack);
        Assert.Equal("partly fixed", ledger.Fixes[ids[0]].Reason);
    }

    [Theory]
    [InlineData(1, "agent")]
    [InlineData(2, "agent")]
    [InlineData(1, "json")]
    [InlineData(2, "json")]
    [InlineData(1, "tests")]
    [InlineData(2, "tests")]
    public async Task FailedAttempts_PersistReasons_AndFeedTheNextWorker(int parallelism, string failure)
    {
        var ids = await SeedAsync("a.cs", 10);
        var packs = new List<string>();
        var calls = 0;
        string Respond(string pack)
        {
            packs.Add(pack);
            calls++;
            if (calls == 1 && failure == "agent") throw new InvalidOperationException("extra file: b.cs");
            if (calls == 1 && failure == "json") return "not json";
            return FixResponseJson.Serialize(new FixResponse("fixed", ids, []));
        }
        if (failure == "tests")
        {
            tests.Results.Enqueue(PassingBaseline);
            tests.Results.Enqueue(new TestRun(false, "a.cs:10 compiler error\nBuild FAILED"));
        }
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(Respond(pack)));
        var editor = new PackFixer(pack => new FileFixEdit(Respond(pack), "a.cs"));
        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor).RunAsync(
            adapter, new FixOptions(Parallelism: parallelism), null, CancellationToken.None);

        Assert.Equal(1, result.Fixed);
        var failed = Assert.Single(ledger.Analyses, a => !a.Analysis.Succeeded).Analysis;
        Assert.Equal(UnitIds.Fix("a.cs"), failed.UnitId);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        foreach (var line in failed.Error.Split('\n')) Assert.Contains(line, packs[1]);
        if (failure == "tests") Assert.Contains("a.cs:10 compiler error", failed.Error);
        Assert.Contains(failed.Error.ReplaceLineEndings(" "), await new Report(ledger, Config.Default).RunAsync(CancellationToken.None));
    }

    private sealed class PackFixer(Func<string, FileFixEdit> run) : IFileFixer
    {
        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => Task.FromResult(run(pack));
    }

    [Fact]
    public async Task ParallelFix_RefillsAnAvailableSlot_AndCommitsEachFileOnce()
    {
        var ids = new Dictionary<string, IReadOnlyList<long>>();
        foreach (var path in new[] { "a.cs", "b.cs", "c.cs" })
        {
            ids[path] = await SeedAsync(path, 10, 20);
        }

        var started = ids.Keys.ToDictionary(path => path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var release = ids.Keys.ToDictionary(path => path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var editor = new FileFixer(async (pack, ct) =>
        {
            started[pack].SetResult();
            await release[pack].Task.WaitAsync(ct);
            return new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids[pack], [])), pack);
        });
        var run = new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor).RunAsync(
            Fixer(_ => throw new Exception("shared-checkout adapter must not run")), new FixOptions(Parallelism: 2), new ListProgress(notes), CancellationToken.None);

        await Task.WhenAll(started["a.cs"].Task, started["b.cs"].Task).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(started["c.cs"].Task.IsCompleted);
        release["b.cs"].SetResult();
        await started["c.cs"].Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(release["a.cs"].Task.IsCompleted);
        release["c.cs"].SetResult();
        release["a.cs"].SetResult();
        var result = await run;

        Assert.Equal((3, 6, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Equal(4, tests.Runs);
        Assert.Equal(3, workspace.CommittedFiles.Distinct().Count());
        Assert.Equal("b.cs", workspace.CommittedFiles[0]);
        Assert.Equal(0, workspace.Restores);
    }

    [Fact]
    public async Task ParallelFix_FailedWorker_ShowsAttemptsRetriesAndGiveUp()
    {
        await SeedAsync("a.cs", 10);
        var editor = new FileFixer((_, _) => throw new InvalidOperationException("extra file: b.cs"));

        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor).RunAsync(
            Fixer(_ => throw new Exception()), new FixOptions(MaxAttempts: 2, Parallelism: 2), new ListProgress(notes), CancellationToken.None);

        Assert.Contains(notes, line => line.Contains("attempt 1/2", StringComparison.Ordinal));
        Assert.Contains(notes, line => line.Contains("attempt 2/2", StringComparison.Ordinal));
        Assert.Contains("queued retry for a.cs", notes);
        Assert.Contains("gave up on a.cs after 2 attempts; findings remain unfixed", notes);
        Assert.Contains(notes, line => line.Contains("extra file: b.cs", StringComparison.Ordinal));
        Assert.Single(result.GaveUp);
        Assert.Empty(ledger.Fixes);
    }

    [Fact]
    public async Task ParallelFix_FailedTestsRetryOnlyThatFile()
    {
        var ids = await SeedAsync("a.cs", 10);
        tests.Results.Enqueue(PassingBaseline);
        tests.Results.Enqueue(new TestRun(false, "broken"));
        tests.Results.Enqueue(new TestRun(true, "passed"));
        var calls = 0;
        var editor = new FileFixer((pack, _) =>
        {
            calls++;
            return Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids, [])), pack));
        });

        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor).RunAsync(
            Fixer(_ => throw new Exception()), new FixOptions(Parallelism: 2), null, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(1, result.Fixed);
        Assert.Equal(new[] { "a.cs" }, workspace.CommittedFiles);
        Assert.Equal(new[] { "a.cs" }, workspace.RestoredFiles);
        Assert.Equal(0, workspace.Restores);
    }

    [Fact]
    public async Task ParallelFix_CancellationStopsWorkersBeforeRestoringTheStash()
    {
        await SeedAsync("a.cs", 10);
        workspace.Clean = false;
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = false;
        var editor = new FileFixer(async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new Exception("unreachable");
            }
            finally
            {
                Assert.Null(workspace.RestoredStash);
                stopped = true;
            }
        });
        var run = new Fix(ledger, tree, clock, Config.Default, workspace, null, editor).RunAsync(
            Fixer(_ => throw new Exception()), new FixOptions(Stash: true, Parallelism: 2), null, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(stopped);
        Assert.Equal("saved-stash", workspace.RestoredStash);
        Assert.Empty(workspace.CommittedFiles);
        Assert.Empty(ledger.Fixes);
    }

    private sealed class FileFixer(Func<string, CancellationToken, Task<FileFixEdit>> run) : IFileFixer
    {
        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => run(path, cancellationToken);
    }

    [Fact]
    public async Task ParallelFix_TenSlotsWithFourFiles_StartsOnlyFourWorkers()
    {
        var ids = new Dictionary<string, IReadOnlyList<long>>();
        foreach (var path in new[] { "a.cs", "b.cs", "c.cs", "d.cs" })
        {
            ids[path] = await SeedAsync(path, 10);
        }

        var started = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = new FileFixer(async (path, ct) =>
        {
            started.Add(path);
            if (started.Count == 4)
            {
                allStarted.SetResult();
            }

            await release.Task.WaitAsync(ct);
            return new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids[path], [])), path);
        });
        var run = new Fix(ledger, tree, clock, Config.Default, workspace, null, editor).RunAsync(
            Fixer(_ => throw new Exception()), new FixOptions(Parallelism: 10), null, CancellationToken.None);
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, started.Count);
        Assert.Equal(4, started.Distinct().Count());
        release.SetResult();

        Assert.Equal(4, (await run).Units);
        Assert.Equal(4, started.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelFix_OneBrokenWorkerDoesNotDiscardAnotherFilesCommit(bool wrongFinding)
    {
        var a = await SeedAsync("a.cs", 10);
        await SeedAsync("b.cs", 10);
        var editor = new FileFixer((path, _) => path == "b.cs" && !wrongFinding
            ? throw new InvalidOperationException("agent failed")
            : Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", a, [])), path)));

        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, null, editor).RunAsync(
            Fixer(_ => throw new Exception()), new FixOptions(MaxAttempts: 1, Parallelism: 2), null, CancellationToken.None);

        Assert.Equal(1, result.Fixed);
        Assert.Equal(new[] { UnitIds.Fix("b.cs") }, result.GaveUp);
        Assert.Equal(new[] { "a.cs" }, workspace.CommittedFiles);
        Assert.Empty(workspace.RestoredFiles);
        Assert.Equal(FixState.Fixed, ledger.Fixes[a[0]].State);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stash_RestoresLocalChanges_OnCancellationOrUnexpectedFailure(bool cancel)
    {
        await SeedAsync("src/a.cs", 10);
        workspace.Clean = false;
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakeAgentAdapter((_, _) =>
        {
            workspace.Clean = false;
            if (cancel)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            throw new InvalidOperationException("agent failed");
        });
        var run = CreateFix(adapter).RunAsync(
            adapter, new FixOptions(1, Stash: true), new ListProgress(notes), cancellation.Token);

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
        else
        {
            Assert.Single((await run).GaveUp);
        }

        Assert.Equal(1, workspace.Stashes);
        Assert.Equal("saved-stash", workspace.RestoredStash);
        Assert.False(workspace.RestoreStashToken.CanBeCanceled);
        Assert.False(workspace.Clean);
    }

    [Fact]
    public async Task Stash_WhenClean_DoesNotCreateOrRestoreAStash()
    {
        var ids = await SeedAsync("src/a.cs", 10);

        var adapter = Fixer(_ => new FixResponse("Fixed", ids, []));
        await CreateFix(adapter).RunAsync(
            adapter, new FixOptions(Stash: true), null, CancellationToken.None);

        Assert.Equal(0, workspace.Stashes);
        Assert.Null(workspace.RestoredStash);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddressedFindingsWithoutAChangedFile_AreRejected_AndTheRetryIsToldWhy(bool alsoDeclines)
    {
        var ids = await SeedAsync("src/a.cs", 10, 20);
        var adapter = Fixer(_ => alsoDeclines
            ? new FixResponse("Fixed one.", [ids[0]], [new DeclinedFix(ids[1], "Not real.")])
            : new FixResponse("Fixed both.", ids, []), touchesFiles: false);

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(2, adapter.Packs.Count);
        Assert.Contains("addressed findings but the file is unchanged; edit it or decline each with a reason", adapter.Packs[1]);
        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.Equal((0, 0, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Empty(workspace.Commits);
        Assert.Empty(ledger.Fixes);
        Assert.DoesNotContain("running tests for src/a.cs", notes);
        Assert.Equal(2, ledger.Analyses.Count(a => !a.Analysis.Succeeded && a.Analysis.Error!.Contains("the file is unchanged", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARepairTheTestCommandUndoes_IsRejected_RestoredAndNeverRecordedFixed()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Fixed.", ids, []));
        // A codegen or restore step in the test command rewrites the assigned file to its committed content.
        tests.OnRun = () => workspace.Clean = true;

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.Equal((0, 0), (result.Units, result.Fixed));
        Assert.Empty(workspace.Commits);
        Assert.Empty(ledger.Fixes);
        Assert.Contains("src/a.cs", workspace.RestoredFiles);
        Assert.Contains("addressed findings but no change remained after validation; edit it or decline each with a reason", adapter.Packs[1]);
    }

    [Fact]
    public async Task AFailingBaseline_StopsBeforeAnyAgentCall_WithItsLastLines_AndRestoresTheStash()
    {
        await SeedAsync("src/a.cs", 10);
        workspace.Clean = false;
        var output = string.Join('\n', ["first line of the suite output", .. Enumerable.Range(1, 30).Select(i => $"test {i} ran"), "Build FAILED: expected 2, got 3"]);
        tests.Results.Enqueue(new TestRun(false, output));
        var adapter = Fixer(_ => throw new InvalidOperationException("no agent call expected"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateFix(adapter, tests).RunAsync(adapter, new FixOptions(Stash: true), new ListProgress(notes), CancellationToken.None));

        Assert.Empty(adapter.Packs);
        Assert.Equal(1, tests.Runs);
        Assert.Contains("Build FAILED: expected 2, got 3", error.Message);
        Assert.DoesNotContain("first line of the suite output", error.Message);
        Assert.Contains("codemuster validate", error.Message);
        Assert.Contains("--allow-failing-tests", error.Message);
        Assert.Equal("saved-stash", workspace.RestoredStash);
        Assert.DoesNotContain(ledger.Analyses, a => !a.Analysis.Succeeded);
    }

    [Fact]
    public async Task AMissingTestProgram_StopsBeforeAnyAgentCall()
    {
        await SeedAsync("src/a.cs", 10);
        var runner = new CallbackTestRunner(() => throw new InvalidOperationException("'no-such-tool' was not found on PATH"));
        var adapter = Fixer(_ => throw new InvalidOperationException("no agent call expected"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(adapter, runner: runner));

        Assert.Empty(adapter.Packs);
        Assert.Contains("'no-such-tool' was not found on PATH", error.Message);
        Assert.Contains("codemuster validate", error.Message);
    }

    [Fact]
    public async Task ABaselineThatChangesTrackedFiles_StopsBeforeAnyAgentCall()
    {
        await SeedAsync("src/a.cs", 10);
        var runner = new CallbackTestRunner(() =>
        {
            workspace.Clean = false;
            workspace.Changed.Add("tests/__snapshots__/a.snap");
            return new TestRun(true, "");
        });
        var adapter = Fixer(_ => throw new InvalidOperationException("no agent call expected"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(adapter, runner: runner));

        Assert.Empty(adapter.Packs);
        Assert.Contains("tests/__snapshots__/a.snap", error.Message);
    }

    [Fact]
    public async Task AllowFailingTests_SkipsTheBaselineCheck()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        tests.Results.Enqueue(new TestRun(false, "the suite is red"));
        var adapter = Fixer(_ => new FixResponse("Fixed it.", ids, []));

        var result = await CreateFix(adapter, tests).RunAsync(adapter, new FixOptions(MaxAttempts: 1) { AllowFailingTests = true }, new ListProgress(notes), CancellationToken.None);

        Assert.Single(adapter.Packs);
        Assert.Equal(1, tests.Runs);
        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.DoesNotContain(notes, note => note.Contains("unmodified tree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APassingBaseline_CostsExactlyOneExtraTestRun_BeforeTheFirstRepair()
    {
        var a = await SeedAsync("src/a.cs", 10);
        var b = await SeedAsync("src/b.cs", 20);
        var adapter = Fixer(pack => new FixResponse("Fixed.", pack.Contains("src/a.cs", StringComparison.Ordinal) ? a : b, []));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal((2, 2), (result.Units, result.Fixed));
        Assert.Equal(3, tests.Runs);
        Assert.Contains("unmodified tree", notes[0]);
        Assert.StartsWith("fixing ", notes[1]);
    }

    [Fact]
    public async Task NothingToFix_RunsNoBaseline()
    {
        var result = await RunAsync(Fixer(_ => throw new InvalidOperationException("no agent call expected")), runner: tests);

        Assert.Equal(0, result.Units);
        Assert.Equal(0, tests.Runs);
    }

    private static readonly TestRun PassingBaseline = new(true, "baseline passes");

    private sealed class CallbackTestRunner(Func<TestRun> run) : ITestRunner
    {
        public Task<TestRun> RunAsync(CancellationToken cancellationToken) => Task.FromResult(run());
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
        Assert.Equal(0, workspace.Restores);
    }

    [Fact]
    public async Task EachUnit_SaysWhatItIsAbout_BeforeItRuns()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Done.", ids, []));

        await RunAsync(adapter);

        Assert.StartsWith("fixing src/a.cs, 1 finding(s)", notes[0]);
        Assert.Contains("finished src/a.cs", notes);
    }

    [Fact]
    public async Task PassingTests_LetTheFixBeRecordedAndCommitted()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Fixed it.", ids, []));
        tests.Results.Enqueue(new TestRun(true, "all good"));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(2, tests.Runs);
        Assert.Equal((1, 1, 0), (result.Units, result.Fixed, result.Declined));
        Assert.Single(workspace.Commits);
        Assert.Contains("running tests for src/a.cs", notes);
    }

    [Fact]
    public async Task FailingTests_ThrowTheFixAway_AndNothingIsRecorded()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Broke it.", ids, []));
        tests.Results.Enqueue(PassingBaseline);
        tests.Results.Enqueue(new TestRun(false, "MyTest failed: expected 2, got 3"));
        tests.Results.Enqueue(new TestRun(false, "MyTest failed: expected 2, got 3"));

        var result = await RunAsync(adapter, runner: tests);

        Assert.Equal(2, adapter.Packs.Count);
        Assert.Equal([UnitIds.Fix("src/a.cs")], result.GaveUp);
        Assert.Equal(0, result.Fixed);
        Assert.Empty(workspace.Commits);
        Assert.Equal(0, workspace.Restores);
        Assert.Empty(ledger.Fixes);
        Assert.Contains(notes, note => note.Contains("expected 2, got 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailingThenPassing_RecordsTheSecondAttempt()
    {
        var ids = await SeedAsync("src/a.cs", 10);
        var adapter = Fixer(_ => new FixResponse("Second time lucky.", ids, []));
        tests.Results.Enqueue(PassingBaseline);
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

        Assert.Equal(2, tests.Runs);
        Assert.True(result.TestsRun);
        Assert.Empty(workspace.Commits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AnOversizedFile_IsSkippedOnce_AndTheOtherFilesAreStillFixed(int parallelism)
    {
        var a = await SeedAsync("a.cs", 10);
        var b = await SeedAsync("b.cs", 20);
        tree.Contents["a.cs"] = new string('x', (Config.Default.SliceTokenBudget * 4) + 1);
        var edited = new List<string>();
        var editor = new FileFixer((path, _) =>
        {
            lock (edited) edited.Add(path);
            return Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", path == "a.cs" ? a : b, [])), path));
        });

        var result = await Fixing(Config.Default, editor)
            .RunAsync(Fixer(_ => throw new Exception()), new FixOptions(Parallelism: parallelism), new ListProgress(notes), CancellationToken.None);

        Assert.Equal(["b.cs"], edited);
        Assert.Equal(1, result.Units);
        Assert.Equal(1, result.Fixed);
        Assert.Empty(result.GaveUp);
        Assert.Equal([UnitIds.Fix("a.cs")], result.Skipped);
        Assert.Equal(UnitStatus.Skipped, ledger.Units.Single(u => u.Id == UnitIds.Fix("a.cs")).Status);
        Assert.Contains(notes, note => note.Contains("a.cs", StringComparison.Ordinal) && note.Contains("split the file, then run fix again; no repair was attempted", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, note => note.Contains("rescan", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, note => note.Contains("fixing a.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(ledger.Analyses, entry => entry.Analysis.UnitId == UnitIds.Fix("a.cs"));
        Assert.DoesNotContain(ledger.Fixes, f => f.Key == a[0]);
    }

    [Fact]
    public async Task AStillOversizedFile_IsSkippedAgain_WithoutSpendingAnAttempt()
    {
        var a = await SeedAsync("a.cs", 10);
        tree.Contents["a.cs"] = new string('x', (Config.Default.SliceTokenBudget * 4) + 1);
        var edited = new List<string>();
        var editor = Editor(a, edited);

        var first = await Fixing(Config.Default, editor).RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), new ListProgress(notes), CancellationToken.None);
        var again = await Fixing(Config.Default, editor).RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), new ListProgress(notes), CancellationToken.None);

        Assert.Equal([UnitIds.Fix("a.cs")], first.Skipped);
        Assert.Equal([UnitIds.Fix("a.cs")], again.Skipped);
        Assert.Empty(again.GaveUp);
        Assert.Empty(edited);
        Assert.DoesNotContain(ledger.Analyses, entry => entry.Analysis.UnitId == UnitIds.Fix("a.cs"));
        Assert.Equal(UnitStatus.Skipped, ledger.Units.Single(u => u.Id == UnitIds.Fix("a.cs")).Status);
    }

    [Fact]
    public async Task ARaisedBudget_LetsAPreviouslySkippedFileBeFixed_WithoutAFlag()
    {
        var a = await SeedAsync("a.cs", 10);
        tree.Contents["a.cs"] = new string('x', (Config.Default.SliceTokenBudget * 4) + 1);
        var edited = new List<string>();
        var editor = Editor(a, edited);

        var first = await Fixing(Config.Default, editor).RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), new ListProgress(notes), CancellationToken.None);
        var bigger = Config.Default with { SliceTokenBudget = Config.Default.SliceTokenBudget * 2 };
        var second = await Fixing(bigger, editor).RunAsync(Fixer(_ => throw new Exception()), new FixOptions(), new ListProgress(notes), CancellationToken.None);

        Assert.Equal([UnitIds.Fix("a.cs")], first.Skipped);
        Assert.Empty(second.Skipped);
        Assert.Equal(1, second.Fixed);
        Assert.Equal(["a.cs"], edited);
        Assert.Equal(UnitStatus.Done, ledger.Units.Single(u => u.Id == UnitIds.Fix("a.cs")).Status);
        Assert.DoesNotContain("pack limit", ledger.Units.Single(u => u.Id == UnitIds.Fix("a.cs")).Summary ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConfirmedFindingFoundAfterAFileWasFixed_IsRepairedByTheNextRun()
    {
        var first = await SeedAsync("a.cs", 10);
        await RunAsync(Fixer(_ => new FixResponse("fixed", first, [])));
        Assert.Equal(UnitStatus.Done, ledger.Units.Single(u => u.Id == UnitIds.Fix("a.cs")).Status);

        var later = await AlsoFoundAsync("a.cs", 20);
        var again = Fixer(_ => new FixResponse("fixed the rest", later, []));
        var result = await CreateFix(again).RunAsync(again, new FixOptions(), null, CancellationToken.None);

        Assert.Equal(1, result.Fixed);
        Assert.Equal("fixed the rest", ledger.Fixes[later[0]].Reason);
    }

    private async Task<IReadOnlyList<long>> AlsoFoundAsync(string path, int line)
    {
        var id = UnitIds.Slice(path + "#later");
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.Slice, path + "#later", Fingerprints.Compute([member]), UnitStatus.Done, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        var finding = new Finding(path, line, line, Severity.High, "correctness", $"claim {line}", "evidence", 0.9, "default");
        await ledger.RecordAnalysisAsync(new Analysis(id, unit.Fingerprint, "lens", At, true, "summary", null), [finding], CancellationToken.None);
        var ids = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Where(f => f.UnitId == id).Select(f => f.Id).ToList();
        foreach (var found in ids)
        {
            ledger.Verifications[found] = new VerifyResponse(Verdict.Confirmed, "it holds");
        }

        return ids;
    }

    private Fix Fixing(Config config, IFileFixer editor) => new(ledger, tree, clock, config, workspace, tests, editor);

    private static FileFixer Editor(IReadOnlyList<long> findings, List<string> edited) => new((path, _) =>
    {
        lock (edited) edited.Add(path);
        return Task.FromResult(new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", findings, [])), path));
    });

    [Fact]
    public async Task AFileThatCannotBeIntegrated_DrainsTheRun_LettingRunningWorkersFinishAndRetainingTheirRepairs()
    {
        var ids = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach (var path in new[] { "a.cs", "b.cs", "c.cs" })
        {
            ids[path] = await SeedAsync(path, 10);
        }

        workspace.Changed.Add("outside.cs");
        var started = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledMidCall = false;
        var editor = new ReleasingFixer(async (path, token) =>
        {
            lock (started)
            {
                started.Add(path);
            }

            if (path != "a.cs")
            {
                try
                {
                    await release.Task.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    cancelledMidCall = true;
                    throw;
                }
            }

            return new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed", ids[path], [])), path, "worktree-" + path);
        });
        var progress = new WatchingProgress(notes, note =>
        {
            if (note.Contains("integration failed for a.cs", StringComparison.Ordinal))
            {
                release.TrySetResult();
            }
        });

        var result = await new Fix(ledger, tree, clock, Config.Default, workspace, tests, editor)
            .RunAsync(Fixer(_ => throw new Exception()), new FixOptions(Parallelism: 2), progress, CancellationToken.None);

        var drained = started.Single(path => path != "a.cs");
        Assert.False(cancelledMidCall);
        Assert.Equal(2, started.Count);
        Assert.Contains("a.cs", started);
        Assert.Equal([UnitIds.Fix("a.cs")], result.GaveUp);
        Assert.Equal(["worktree-a.cs"], editor.Released);
        Assert.Contains(notes, note => note.Contains(drained, StringComparison.Ordinal) && note.Contains("worktree-" + drained, StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("changed while validation ran, written either by the test command or by something else using this checkout: outside.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(drained, workspace.CommittedFiles);
        Assert.False(ledger.Fixes.ContainsKey(ids[drained][0]));
    }

    private sealed class WatchingProgress(List<string> lines, Action<string> onNote) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (lines)
            {
                lines.Add(value);
            }

            onNote(value);
        }
    }

    private sealed class ReleasingFixer(Func<string, CancellationToken, Task<FileFixEdit>> run) : IFileFixer
    {
        public List<string> Released { get; } = [];

        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => run(path, cancellationToken);

        public Task ReleaseAsync(FileFixEdit edit, CancellationToken cancellationToken)
        {
            lock (Released)
            {
                Released.Add(edit.Worktree!);
            }

            return Task.CompletedTask;
        }
    }
}
