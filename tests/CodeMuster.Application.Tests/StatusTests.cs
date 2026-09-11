using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class StatusTests
{
    private const string Head = "abcdef0123456789abcdef0123456789abcdef01";
    private const string At = "2026-09-10T12:00:00.0000000Z";

    private readonly FakeLedger ledger = new();

    private Task<StatusReport> RunAsync() => new Status(ledger, Config.Default).RunAsync(CancellationToken.None);

    private void AddUnit(string path, UnitStatus status, Fidelity fidelity = Fidelity.Full, UnitKind kind = UnitKind.File) =>
        ledger.Units.Add(new Unit(kind + ":" + path, kind, path, "fp-" + path, status, fidelity, null, null, null));

    private void AddFile(string path, string? excludedReason = null, string? deletedAt = null) =>
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "hash-" + path, 10, At, At, At, null, null, excludedReason, deletedAt, null, null);

    private void Seed()
    {
        AddUnit("src/a.cs", UnitStatus.Pending);
        AddUnit("src/b.cs", UnitStatus.Done);
        AddUnit("src/c.cs", UnitStatus.Done, Fidelity.Low);
        AddUnit("src/d.cs", UnitStatus.Stale);
        AddUnit("src/e.cs", UnitStatus.Failed);
        AddUnit("src/f.cs", UnitStatus.Retired, Fidelity.Low);
        AddFile("src/a.cs");
        AddFile("src/Gen.g.cs", "generated");
        AddFile("src/Old.g.cs", "generated", At);
        AddFile("src/f.cs", null, At);
    }

    [Fact]
    public async Task Counts_MatchTheSeededLedger_IgnoringRetiredUnitsAndDeletedFiles()
    {
        Seed();
        ledger.Runs.Add(new ScanRun(At, Head, 4, 1, 5, 0.973));

        var report = await RunAsync();

        Assert.Equal(Head, report.HeadCommit);
        Assert.Equal((2, 5, 1, 1, 1), (report.Analyzed, report.Total, report.Stale, report.Excluded, report.LowFidelity));
        Assert.Equal(0.973, report.ResolutionRate);
        Assert.Equal([new KindStatus(UnitKind.File, 2, 5, 1)], report.Kinds);
    }

    [Fact]
    public async Task Render_IsTheExactPlainTextBlock()
    {
        Seed();
        ledger.Runs.Add(new ScanRun(At, Head, 4, 1, 5, 0.973));

        var text = (await RunAsync()).Render();

        Assert.Equal("analyzed 2/5 at abcdef0\nfile 2/5, 1 stale\nstale 1\nexcluded 1\nlow-fidelity 1\nresolution 97.3%", text);
    }

    [Fact]
    public async Task BeforeAnyScan_HeadIsNull_AndRenderSaysNoScanYet()
    {
        var report = await RunAsync();

        Assert.Null(report.HeadCommit);
        Assert.Empty(report.Kinds);
        Assert.Equal("analyzed 0/0 at no scan yet\nstale 0\nexcluded 0\nlow-fidelity 0", report.Render());
    }

    [Fact]
    public async Task ResolutionLine_IsOmitted_WhenTheRunHasNoRate()
    {
        Seed();
        ledger.Runs.Add(new ScanRun(At, Head, 4, 1, 5, 0.5));
        ledger.Runs.Add(new ScanRun(At, Head, 4, 1, 5, null));

        var report = await RunAsync();

        Assert.Null(report.ResolutionRate);
        Assert.Equal("analyzed 2/5 at abcdef0\nfile 2/5, 1 stale\nstale 1\nexcluded 1\nlow-fidelity 1", report.Render());
    }

    [Fact]
    public async Task KindLines_CountDoneTotalAndStalePerKind_InKindOrder()
    {
        AddUnit("GET /quotes", UnitStatus.Done, kind: UnitKind.Slice);
        AddUnit("GET /quotes/{id}", UnitStatus.Stale, kind: UnitKind.Slice);
        AddUnit("src/Services/QuoteService.cs", UnitStatus.Pending, kind: UnitKind.Orphan);
        AddUnit("src/Program.cs", UnitStatus.Done);
        ledger.Runs.Add(new ScanRun(At, Head, 3, 0, 4, 1.0, []));

        var text = (await RunAsync()).Render();

        Assert.Equal("analyzed 2/4 at abcdef0\nfile 1/1\nslice 1/2, 1 stale\norphan 0/1\nstale 1\nexcluded 0\nlow-fidelity 0\nresolution 100.0%", text);
    }

    [Fact]
    public async Task Complete_WhenEveryUnitIsDone_AndResolutionMeetsTheThreshold()
    {
        AddUnit("GET /quotes", UnitStatus.Done, kind: UnitKind.Slice);
        AddUnit("src/Program.cs", UnitStatus.Done);
        ledger.Runs.Add(new ScanRun(At, Head, 2, 0, 2, 0.95, []));

        var text = (await RunAsync()).Render();

        Assert.EndsWith("\nresolution 95.0%\ncomplete", text);
    }

    [Fact]
    public async Task Complete_WhenEveryUnitIsDone_AndNoMapperRan()
    {
        AddUnit("src/Program.cs", UnitStatus.Done);
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, null));

        Assert.EndsWith("\nlow-fidelity 0\ncomplete", (await RunAsync()).Render());
    }

    [Fact]
    public async Task Incomplete_WhenResolutionIsBelowTheThreshold_EvenWithEveryUnitDone()
    {
        AddUnit("GET /quotes", UnitStatus.Done, kind: UnitKind.Slice);
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, 0.42, ["Send", "Publish"]));

        var text = (await RunAsync()).Render();

        Assert.EndsWith("\nresolution 42.0%\nincomplete: resolution 42.0% is below the 90.0% threshold\ntop unresolved: Send, Publish", text);
        Assert.DoesNotContain("\ncomplete", text);
    }
}
