using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class StatusTests
{
    private const string Head = "abcdef0123456789abcdef0123456789abcdef01";
    private const string At = "2026-09-10T12:00:00.0000000Z";

    private readonly FakeLedger ledger = new();

    private Task<StatusReport> RunAsync() => new Status(ledger).RunAsync(CancellationToken.None);

    private void AddUnit(string path, UnitStatus status, Fidelity fidelity = Fidelity.Full) =>
        ledger.Units.Add(new Unit(UnitIds.File(path), UnitKind.File, path, "fp-" + path, status, fidelity, null, null, null));

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
        ledger.Runs.Add(new Domain.Run(At, Head, 4, 1, 5, 0.973));

        var report = await RunAsync();

        Assert.Equal(new StatusReport(Head, 2, 5, 1, 1, 1, 0.973), report);
    }

    [Fact]
    public async Task Render_IsTheExactPlainTextBlock()
    {
        Seed();
        ledger.Runs.Add(new Domain.Run(At, Head, 4, 1, 5, 0.973));

        var text = (await RunAsync()).Render();

        Assert.Equal("analyzed 2/5 at abcdef0\nstale 1\nexcluded 1\nlow-fidelity 1\nresolution 97.3%", text);
    }

    [Fact]
    public async Task BeforeAnyScan_HeadIsNull_AndRenderSaysNoScanYet()
    {
        var report = await RunAsync();

        Assert.Equal(new StatusReport(null, 0, 0, 0, 0, 0, null), report);
        Assert.Equal("analyzed 0/0 at no scan yet\nstale 0\nexcluded 0\nlow-fidelity 0", report.Render());
    }

    [Fact]
    public async Task ResolutionLine_IsOmitted_WhenTheRunHasNoRate()
    {
        Seed();
        ledger.Runs.Add(new Domain.Run(At, Head, 4, 1, 5, 0.5));
        ledger.Runs.Add(new Domain.Run(At, Head, 4, 1, 5, null));

        var report = await RunAsync();

        Assert.Null(report.ResolutionRate);
        Assert.Equal("analyzed 2/5 at abcdef0\nstale 1\nexcluded 1\nlow-fidelity 1", report.Render());
    }
}
