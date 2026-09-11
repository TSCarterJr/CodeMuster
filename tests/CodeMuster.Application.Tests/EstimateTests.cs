using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class EstimateTests
{
    private const string At = "2026-09-10T12:00:00.0000000Z";

    private readonly FakeLedger ledger = new();

    private Task<EstimateReport> RunAsync(Config? config = null) => new Estimate(ledger, config ?? Config.Default).RunAsync(CancellationToken.None);

    private void AddUnit(string id, UnitKind kind, UnitStatus status, params string[] paths)
    {
        ledger.Units.Add(new Unit(id, kind, id, "fp-" + id, status, Fidelity.Full, null, null, null));
        ledger.Members.AddRange(paths.Select(path => new UnitMember(id, path, null, "hash-" + path, 0)));
    }

    private void AddFile(string path, long size) =>
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "hash-" + path, size, At, At, At, null, null, null, null, null, null);

    private void Seed()
    {
        AddFile("src/a.cs", 4001);
        AddFile("src/b.cs", 803);
        AddFile("src/c.cs", 41);
        AddFile("src/d.cs", 8000);
        AddFile("src/e.cs", 8000);
        AddUnit("file:src/a.cs", UnitKind.File, UnitStatus.Pending, "src/a.cs");
        AddUnit("file:src/b.cs", UnitKind.File, UnitStatus.Stale, "src/b.cs");
        AddUnit("file:src/c.cs", UnitKind.File, UnitStatus.Failed, "src/c.cs");
        AddUnit("file:src/z.cs", UnitKind.File, UnitStatus.Pending, "src/z.cs");
        AddUnit("file:src/d.cs", UnitKind.File, UnitStatus.Done, "src/d.cs");
        AddUnit("file:src/e.cs", UnitKind.File, UnitStatus.Retired, "src/e.cs");
        AddUnit("slice:GET /v1/quotes", UnitKind.Slice, UnitStatus.Pending, "src/a.cs", "src/b.cs");
    }

    [Fact]
    public async Task PendingStaleAndFailedUnits_AreSummedPerKind_AtBytesOverFourPlusPackOverhead()
    {
        Seed();

        var report = await RunAsync();

        Assert.Equal(700, Estimate.PackOverheadTokens);
        Assert.Equal(
            [new EstimateLine(UnitKind.File, 4, 1210 + 4 * 700), new EstimateLine(UnitKind.Slice, 1, 1201 + 700)],
            report.Lines);
        Assert.Equal(5911, report.TotalTokens);
        Assert.Equal("file 4 units ~4010 tokens\nslice 1 units ~1901 tokens\ntotal ~5911 tokens", report.Render());
    }

    [Fact]
    public async Task SymbolMembers_CountTokensPerLine_AndSlicesCapAtTheBudgetButNeverBelowTheEntryPoint()
    {
        ledger.Units.Add(new Unit("slice:M:Api.Get", UnitKind.Slice, "GET /x", "fp-slice", UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.AddRange(
        [
            new UnitMember("slice:M:Api.Get", "src/Api.cs", "M:Api.Get", "h1", 0, new LineRange(1, 20), "s"),
            new UnitMember("slice:M:Api.Get", "src/Svc.cs", "M:Svc.Run", "h2", 1, new LineRange(1, 50), "s"),
            new UnitMember("slice:M:Api.Get", "src/Db.cs", "M:Db.Load", "h3", 2, new LineRange(1, 100), "s"),
        ]);
        ledger.Units.Add(new Unit("orphan:src/Svc.cs", UnitKind.Orphan, "src/Svc.cs", "fp-orphan", UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.AddRange(
        [
            new UnitMember("orphan:src/Svc.cs", "src/Svc.cs", "M:Svc.Old", "h4", 0, new LineRange(60, 69), "s"),
            new UnitMember("orphan:src/Svc.cs", "src/Svc.cs", "M:Svc.Older", "h5", 0, new LineRange(70, 74), "s"),
        ]);

        var report = await RunAsync(Config.Default with { SliceTokenBudget = 600 });

        Assert.Equal(10, Estimate.TokensPerLine);
        Assert.Equal([new EstimateLine(UnitKind.Slice, 1, 600 + 700), new EstimateLine(UnitKind.Orphan, 1, 150 + 700)], report.Lines);
        Assert.Equal(200 + 700, (await RunAsync(Config.Default with { SliceTokenBudget = 100 })).Lines[0].Tokens);
    }

    [Fact]
    public async Task DoneAndRetiredUnits_AreNotCounted()
    {
        AddFile("src/d.cs", 8000);
        AddFile("src/e.cs", 8000);
        AddUnit("file:src/d.cs", UnitKind.File, UnitStatus.Done, "src/d.cs");
        AddUnit("file:src/e.cs", UnitKind.File, UnitStatus.Retired, "src/e.cs");

        var report = await RunAsync();

        Assert.Empty(report.Lines);
        Assert.Equal(0, report.TotalTokens);
    }

    [Fact]
    public async Task EmptyLedger_RendersZeroTotalOnly()
    {
        var report = await RunAsync();

        Assert.Empty(report.Lines);
        Assert.Equal("total ~0 tokens", report.Render());
    }
}
