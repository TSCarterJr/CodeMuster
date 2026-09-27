using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerCodeMapTests
{
    private const string ListQuotes = "M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32)";
    private const string ServiceList = "M:MixedRepo.Api.Services.QuoteService.ListQuotes(System.Int32)";
    private const string RepositoryCtor = "M:MixedRepo.Api.Data.QuoteRepository.#ctor";
    private const string FetchQuotes = "web/src/api.ts#fetchQuotes";
    private const string StoreLoad = "web/src/store.ts#QuoteStore.load";

    private static StoredCodeMap First() => new(
        "aaa111",
        "2026-09-27T10:00:00.0000000Z",
        new CodeMap(
            [
                new Symbol(ListQuotes, "src/Api/QuotesController.cs", new LineRange(9, 13), "method", "[ApiController] public sealed class QuotesController\n[HttpGet(\"quotes\")] public IReadOnlyList<Quote> ListQuotes(int tenantId)", "hash-1"),
                new Symbol(ServiceList, "src/Api/QuoteService.cs", new LineRange(12, 15), "method", "public sealed class QuoteService\npublic IReadOnlyList<Quote> ListQuotes(int tenantId)", "hash-2"),
                new Symbol(RepositoryCtor, "src/Api/QuoteRepository.cs", new LineRange(7, 15), "constructor", "public sealed class QuoteRepository\npublic QuoteRepository()", "hash-3"),
                new Symbol(FetchQuotes, "web/src/api.ts", new LineRange(1, 4), "function", "export async function fetchQuotes(): Promise<Quote[]>", "hash-4"),
                new Symbol(StoreLoad, "web/src/store.ts", new LineRange(3, 8), "method", "export class QuoteStore\nload(): void", "hash-5"),
            ],
            [
                new Edge(ListQuotes, ServiceList, EdgeKind.Bound),
                new Edge(ListQuotes, ServiceList, EdgeKind.Bound),
                new Edge(ServiceList, RepositoryCtor, EdgeKind.Call),
                new Edge(StoreLoad, FetchQuotes, EdgeKind.Call),
                new Edge(ServiceList, ServiceList, EdgeKind.Overrides),
                new Edge(ListQuotes, RepositoryCtor, EdgeKind.Implements),
            ],
            [
                new EntryPoint(ListQuotes, "http", "GET /quotes"),
                new EntryPoint(StoreLoad, "page", "/quotes"),
            ],
            new ResolutionStats(21, 2, ["GetService", "fetch"]),
            ["typescript: no jsx factory", "csharp mapper failed: line one\nline two"]),
        ["csharp", "typescript"],
        ["python"]);

    private static void AssertSame(StoredCodeMap expected, StoredCodeMap? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.HeadCommit, expected.ScannedAt), (actual.HeadCommit, actual.ScannedAt));
        Assert.Equal(expected.Map.Symbols, actual.Map.Symbols);
        Assert.Equal(expected.Map.Edges, actual.Map.Edges);
        Assert.Equal(expected.Map.EntryPoints, actual.Map.EntryPoints);
        Assert.Equal((expected.Map.Resolution.Resolved, expected.Map.Resolution.Unresolved), (actual.Map.Resolution.Resolved, actual.Map.Resolution.Unresolved));
        Assert.Equal(expected.Map.Resolution.TopUnresolvedNames, actual.Map.Resolution.TopUnresolvedNames);
        Assert.Equal(expected.Map.Diagnostics, actual.Map.Diagnostics);
        Assert.Equal(expected.MappedLanguages, actual.MappedLanguages);
        Assert.Equal(expected.FailedLanguages, actual.FailedLanguages);
    }

    [Fact]
    public async Task GetCodeMap_IsNullBeforeAnyMapWasStored()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Null(await ledger.GetCodeMapAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceCodeMap_RoundTripsSymbolsEdgesEntryPointsAndDiagnostics_InOrderWithDuplicates()
    {
        using var temp = new TempDirectory();
        using (var writer = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await writer.ReplaceCodeMapAsync(First(), CancellationToken.None);
        }

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var stored = await ledger.GetCodeMapAsync(CancellationToken.None);

        AssertSame(First(), stored);
        Assert.True(stored!.IsPartial);
    }

    [Fact]
    public async Task GetCodeMap_SkipsAnEdgeKindThisBuildDoesNotKnow_AndSaysSoInTheDiagnostics()
    {
        using var temp = new TempDirectory();
        using (var writer = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await writer.ReplaceCodeMapAsync(First(), CancellationToken.None);
        }

        await RawSqlite.ExecuteAsync(temp.DatabasePath, "UPDATE code_edges SET kind = 'teleport' WHERE rowid IN (SELECT rowid FROM code_edges ORDER BY rowid LIMIT 2)");
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var stored = await ledger.GetCodeMapAsync(CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(First().Map.Edges.Skip(2), stored.Map.Edges);
        Assert.Contains("2 edge(s) of kind 'teleport' are unknown to this version of codemuster and were skipped", stored.Map.Diagnostics);
        Assert.True(stored.IsPartial);
    }

    [Fact]
    public async Task ReplaceCodeMap_StoresEachSymbolsContainer_AndNoSourceText()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        await ledger.ReplaceCodeMapAsync(First(), CancellationToken.None);

        Assert.Equal(
            ["MixedRepo.Api.Controllers.QuotesController", "MixedRepo.Api.Services.QuoteService", "MixedRepo.Api.Data.QuoteRepository", "web/src/api.ts", "web/src/store.ts#QuoteStore"],
            await RawSqlite.StringsAsync(temp.DatabasePath, "SELECT container FROM code_symbols ORDER BY rowid"));
        Assert.Equal(
            ["id", "path", "start_line", "end_line", "kind", "signature", "body_hash", "container"],
            await RawSqlite.StringsAsync(temp.DatabasePath, "SELECT name FROM pragma_table_info('code_symbols') ORDER BY cid"));
    }

    [Fact]
    public async Task ReplaceCodeMap_ReplacesThePreviousMap_RatherThanAccumulating()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        await ledger.ReplaceCodeMapAsync(First(), CancellationToken.None);
        var second = new StoredCodeMap(
            "bbb222",
            "2026-09-27T11:00:00.0000000Z",
            new CodeMap(
                [new Symbol(FetchQuotes, "web/src/api.ts", new LineRange(2, 6), "function", "export async function fetchQuotes()", "hash-9")],
                [],
                [],
                new ResolutionStats(0, 0, []),
                []),
            ["typescript"],
            []);

        await ledger.ReplaceCodeMapAsync(second, CancellationToken.None);

        var stored = await ledger.GetCodeMapAsync(CancellationToken.None);
        AssertSame(second, stored);
        Assert.False(stored!.IsPartial);
        foreach (var table in new[] { "code_map", "code_symbols" })
        {
            Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, $"SELECT COUNT(*) FROM {table}"));
        }

        foreach (var table in new[] { "code_edges", "code_entry_points" })
        {
            Assert.Equal(0L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, $"SELECT COUNT(*) FROM {table}"));
        }
    }

    [Fact]
    public async Task ReplaceCodeMap_LeavesUnitsFilesAndRunsAlone()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = new Unit("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([unit], [new UnitMember(unit.Id, "src/A.cs", null, "h1", 0)], CancellationToken.None);
        var run = new ScanRun("2026-09-27T10:00:00.0000000Z", "aaa111", 1, 0, 1, 1.0, []);
        await ledger.RecordRunAsync(run, CancellationToken.None);

        await ledger.ReplaceCodeMapAsync(First(), CancellationToken.None);

        Assert.Equal([unit], await ledger.GetUnitsAsync(CancellationToken.None));
        Assert.Equal([unit], await ledger.NextAsync(10, null, null, CancellationToken.None));
        Assert.Single(await ledger.GetMembersAsync([unit.Id], CancellationToken.None));
        Assert.Equal(run.HeadCommit, (await ledger.GetLastRunAsync(CancellationToken.None))!.HeadCommit);
    }

    [Fact]
    public async Task ReplaceCodeMap_IsOneTransaction_SoAFailedWriteKeepsThePreviousMap()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        await ledger.ReplaceCodeMapAsync(First(), CancellationToken.None);
        var map = First().Map;
        var large = First() with
        {
            HeadCommit = "ccc333",
            Map = map with { Edges = [.. Enumerable.Range(0, 5000).Select(_ => map.Edges[0])] },
        };
        await RawSqlite.ExecuteAsync(temp.DatabasePath, """
            CREATE TRIGGER refuse_entry AFTER INSERT ON code_entry_points BEGIN SELECT RAISE(ABORT, 'refused'); END;
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => ledger.ReplaceCodeMapAsync(large, CancellationToken.None));

        await RawSqlite.ExecuteAsync(temp.DatabasePath, "DROP TRIGGER refuse_entry");
        AssertSame(First(), await ledger.GetCodeMapAsync(CancellationToken.None));
    }
}
