using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerUnitTests
{
    private const string At = "2026-09-10T03:00:00.0000000Z";

    [Fact]
    public async Task GetUnit_ReturnsNullForUnknownId()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Null(await ledger.GetUnitAsync("file:src/Missing.cs", CancellationToken.None));
        Assert.Empty(await ledger.GetUnitsAsync(CancellationToken.None));
        Assert.Empty(await ledger.GetMembersAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task UpsertUnits_InsertsUnitsAndMembersAndRoundTripsEveryColumn()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var slice = new Unit("slice:Api.Run", UnitKind.Slice, "Api.Run", "fp-slice", UnitStatus.Done, Fidelity.Low, "lens-1", "Runs the API", "fp-slice");
        var file = Pending("src/A.cs");
        var members = new[]
        {
            new UnitMember(slice.Id, "src/Api.cs", "Api.Run", "h1", 0, new LineRange(3, 9), "public sealed class Api\npublic void Run()"),
            new UnitMember(slice.Id, "src/Db.cs", "Db.Query", "h2", 1, new LineRange(12, 20), "public sealed class Db\npublic int Query()"),
            Member(file, "h3"),
        };

        await ledger.UpsertUnitsAsync([slice, file], members, CancellationToken.None);

        Assert.Equal([slice, file], await ledger.GetUnitsAsync(CancellationToken.None));
        Assert.Equal(slice, await ledger.GetUnitAsync(slice.Id, CancellationToken.None));
        Assert.Equal(members, await ledger.GetMembersAsync([slice.Id, file.Id], CancellationToken.None));
        Assert.Equal([members[2]], await ledger.GetMembersAsync([file.Id], CancellationToken.None));
    }

    [Fact]
    public async Task UpsertUnits_ReplacesMembersOfTheGivenUnitsOnly()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var a = Pending("src/A.cs");
        var b = Pending("src/B.cs");
        await ledger.UpsertUnitsAsync([a, b], [Member(a, "a1"), Member(b, "b1")], CancellationToken.None);

        await ledger.UpsertUnitsAsync([a], [Member(a, "a2")], CancellationToken.None);

        Assert.Equal([Member(a, "a2")], await ledger.GetMembersAsync([a.Id], CancellationToken.None));
        Assert.Equal([Member(b, "b1")], await ledger.GetMembersAsync([b.Id], CancellationToken.None));
    }

    [Fact]
    public async Task UpsertUnits_ReplacesEveryColumnButKeepsInsertionPosition()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var a = Pending("src/A.cs");
        var b = Pending("src/B.cs");
        var c = Pending("src/C.cs");
        await ledger.UpsertUnitsAsync([a, b, c], [], CancellationToken.None);

        var changed = a with { Fingerprint = "fp-2", Status = UnitStatus.Stale, Fidelity = Fidelity.Low, LensHash = "lens-1", Summary = "Summary", SummaryHash = "fp-1" };
        await ledger.UpsertUnitsAsync([changed], [], CancellationToken.None);

        Assert.Equal([changed, b, c], await ledger.GetUnitsAsync(CancellationToken.None));
        Assert.Equal([changed, b, c], await ledger.NextAsync(10, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Next_WithAKind_ReturnsOnlyUnitsOfThatKind()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var file = Pending("src/A.cs");
        var verify = new Unit(UnitIds.Verify(1), UnitKind.Verify, "src/A.cs:1", "fp", UnitStatus.Failed, Fidelity.Full, null, null, null);
        var orphan = new Unit(UnitIds.Orphan("src/B.cs"), UnitKind.Orphan, "src/B.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([file, verify, orphan], [], CancellationToken.None);

        Assert.Equal([verify], await ledger.NextAsync(10, UnitKind.Verify, null, CancellationToken.None));
        Assert.Equal([file], await ledger.NextAsync(10, UnitKind.File, null, CancellationToken.None));
        Assert.Empty(await ledger.NextAsync(10, UnitKind.Slice, null, CancellationToken.None));
    }

    [Fact]
    public async Task Next_ReturnsPendingStaleAndFailedInInsertionOrderAndHonoursBatch()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var done = Pending("src/Done.cs") with { Status = UnitStatus.Done };
        var stale = Pending("src/Stale.cs") with { Status = UnitStatus.Stale };
        var retired = Pending("src/Retired.cs") with { Status = UnitStatus.Retired };
        var failed = Pending("src/Failed.cs") with { Status = UnitStatus.Failed };
        var pending = Pending("src/Pending.cs");
        await ledger.UpsertUnitsAsync([done, stale, retired, failed, pending], [], CancellationToken.None);

        Assert.Equal([stale, failed, pending], await ledger.NextAsync(10, null, null, CancellationToken.None));
        Assert.Equal([stale, failed], await ledger.NextAsync(2, null, null, CancellationToken.None));
        Assert.Empty(await ledger.NextAsync(0, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetMembers_HandlesMoreIdsThanOneStatementCanBind()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var units = Enumerable.Range(0, 1201).Select(i => Pending($"src/F{i}.cs")).ToList();
        await ledger.UpsertUnitsAsync(units, units.Select(u => Member(u, "h")).ToList(), CancellationToken.None);

        var members = await ledger.GetMembersAsync(units.Select(u => u.Id).ToList(), CancellationToken.None);

        Assert.Equal(1201, members.Count);
        Assert.Equal(1201, members.Select(m => m.UnitId).Distinct().Count());
    }

    [Fact]
    public async Task RecordAnalysis_SuccessMovesUnitToDoneAndStoresFindings()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = Pending("src/A.cs");
        await ledger.UpsertUnitsAsync([unit], [Member(unit, "h")], CancellationToken.None);
        var analysis = new Analysis(unit.Id, "fp-2", "lens-1", At, true, "Does A", null);
        var findings = new[]
        {
            new Finding("src/A.cs", 1, 2, Severity.High, "security", "claim", "evidence", 0.9, "lens-a"),
            new Finding("src/A.cs", 5, 5, Severity.Info, "style", "claim 2", "evidence 2", 0.5, "lens-a"),
        };

        await ledger.RecordAnalysisAsync(analysis, findings, CancellationToken.None);

        Assert.Equal(unit with { Status = UnitStatus.Done, Summary = "Does A", SummaryHash = "fp-2", LensHash = "lens-1" }, await ledger.GetUnitAsync(unit.Id, CancellationToken.None));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM analyses WHERE unit_id = 'file:src/A.cs' AND fingerprint = 'fp-2' AND lens_hash = 'lens-1' AND succeeded = 1 AND summary = 'Does A' AND error IS NULL"));
        Assert.Equal(2L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM findings WHERE analysis_id = (SELECT id FROM analyses)"));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM findings WHERE path = 'src/A.cs' AND line_start = 1 AND line_end = 2 AND severity = 'high' AND category = 'security' AND claim = 'claim' AND evidence = 'evidence' AND confidence = 0.9 AND lens_id = 'lens-a'"));
    }

    [Fact]
    public async Task RecordAnalysis_FailureMovesUnitToFailedAndStoresError()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = Pending("src/A.cs") with { Status = UnitStatus.Done, Summary = "Old", SummaryHash = "fp-0", LensHash = "lens-0" };
        await ledger.UpsertUnitsAsync([unit], [Member(unit, "h")], CancellationToken.None);

        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, "fp-1", "lens-1", At, false, null, "invalid json"), [], CancellationToken.None);

        Assert.Equal(unit with { Status = UnitStatus.Failed }, await ledger.GetUnitAsync(unit.Id, CancellationToken.None));
        Assert.Equal("invalid json", await RawSqlite.ScalarAsync<string>(temp.DatabasePath, "SELECT error FROM analyses WHERE succeeded = 0 AND summary IS NULL"));
        Assert.Equal(0L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM findings"));
    }

    [Fact]
    public async Task TwoLedgers_OnOneFileInterleaveWritesWithoutError()
    {
        using var temp = new TempDirectory();
        using var first = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        using var second = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        await Task.WhenAll(Task.Run(() => AnalyzeManyAsync(first, "a")), Task.Run(() => AnalyzeManyAsync(second, "b")));

        var units = await first.GetUnitsAsync(CancellationToken.None);
        Assert.Equal(50, units.Count);
        Assert.All(units, u => Assert.Equal(UnitStatus.Done, u.Status));
        Assert.Equal(50, (await second.GetMembersAsync(units.Select(u => u.Id).ToList(), CancellationToken.None)).Count);
        Assert.Empty(await second.NextAsync(100, null, null, CancellationToken.None));
    }

    private static async Task AnalyzeManyAsync(SqliteLedger ledger, string prefix)
    {
        for (var i = 0; i < 25; i++)
        {
            var unit = Pending($"src/{prefix}{i}.cs");
            await ledger.UpsertUnitsAsync([unit], [Member(unit, "h")], CancellationToken.None);
            await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "Summary", null), [], CancellationToken.None);
        }
    }

    private static Unit Pending(string path) =>
        new(UnitIds.File(path), UnitKind.File, path, "fp-" + path, UnitStatus.Pending, Fidelity.Full, null, null, null);

    private static UnitMember Member(Unit unit, string hash) => new(unit.Id, unit.Key, null, hash, 0);
}
