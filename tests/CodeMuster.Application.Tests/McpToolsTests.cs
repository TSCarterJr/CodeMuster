using System.Text.Json.Nodes;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class McpToolsTests
{
    private const string Controller = "M:Shop.Web.OrdersController.Get(System.Int32)";
    private const string Service = "M:Shop.Services.OrderService.Load(System.Int32)";
    private const string Audit = "M:Shop.Services.AuditService.Load(System.Int32)";
    private const string Repository = "M:Shop.Data.OrderRepository.Find(System.Int32)";
    private const string Client = "web/src/orders.ts#loadOrder";
    private const string Page = "web/app/orders/page.tsx#OrdersPage";
    private const string Order = "T:Shop.Models.Order";
    private const string Total = "P:Shop.Models.Order.Total";

    private const string ControllerPath = "src/Web/OrdersController.cs";
    private const string ServicePath = "src/Services/OrderService.cs";
    private const string AuditPath = "src/Services/AuditService.cs";
    private const string RepositoryPath = "src/Data/OrderRepository.cs";
    private const string ClientPath = "web/src/orders.ts";
    private const string PagePath = "web/app/orders/page.tsx";
    private const string ModelPath = "src/Models/Order.cs";

    private static readonly string[] Paths = [ControllerPath, ServicePath, AuditPath, RepositoryPath, ClientPath, PagePath, ModelPath];

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeContentHasher hasher = new();

    private static Symbol Method(string id, string path, int line, string signature, string? normalized = null) =>
        new(id, path, new LineRange(line, line + 7), "method", signature, "hash-" + id, normalized);

    private static CodeMap Map(bool references = true) => new(
        [
            Method(Controller, ControllerPath, 10, "public sealed class OrdersController\n[HttpGet(\"orders/{id}\")] public Order Get(int id)"),
            Method(Service, ServicePath, 12, "public sealed class OrderService : IOrderService\npublic Order Load(int id)", "same-body"),
            Method(Audit, AuditPath, 5, "public sealed class AuditService\npublic Order Load(int id)", "same-body"),
            Method(Repository, RepositoryPath, 20, "public sealed class OrderRepository\npublic Order Find(int id)"),
            new Symbol(Client, ClientPath, new LineRange(3, 12), "function", "export async function loadOrder(id: number)", "hash-client"),
            new Symbol(Page, PagePath, new LineRange(1, 20), "function", "export default function OrdersPage()", "hash-page"),
        ],
        [
            new Edge(Page, Client, EdgeKind.Call),
            new Edge(Client, Controller, EdgeKind.Http),
            new Edge(Controller, Service, EdgeKind.Bound),
            new Edge(Service, Repository, EdgeKind.Call),
            new Edge(Audit, Repository, EdgeKind.Call),
        ],
        [
            new EntryPoint(Controller, "http", "GET /orders/{id}"),
            new EntryPoint(Page, "page", "/orders"),
        ],
        new ResolutionStats(5, 0, []),
        ["http: web/src/orders.ts:9 POST /orders/{p} matches no endpoint"])
    {
        Declarations = references
            ?
            [
                new Symbol(Order, ModelPath, new LineRange(3, 9), "class", "public sealed class Order", "hash-order"),
                new Symbol(Total, ModelPath, new LineRange(5, 5), "property", "public sealed class Order\npublic decimal Total { get; set; }", "hash-total"),
            ]
            : [],
        References = references
            ?
            [
                new Reference(Service, Repository, ReferenceKind.Call, ServicePath, 14, 16),
                new Reference(Audit, Repository, ReferenceKind.Call, AuditPath, 7, 9),
                new Reference(Repository, Total, ReferenceKind.Write, RepositoryPath, 22, 13),
                new Reference(Service, Total, ReferenceKind.Read, ServicePath, 15, 20),
                new Reference(Controller, Order, ReferenceKind.Type, ControllerPath, 11, 12),
            ]
            : [],
    };

    private async Task StoreAsync(CodeMap map, string scannedAt = "2026-09-28T10:00:00.0000000Z")
    {
        await ledger.ReplaceCodeMapAsync(new StoredCodeMap("abcdef1234567890", scannedAt, map, [Languages.CSharp, Languages.TypeScript], []), CancellationToken.None);
        await ledger.RecordRunAsync(new ScanRun(scannedAt, "abcdef1234567890", Paths.Length, 0, 10, 1.0), CancellationToken.None);
    }

    private async Task StoreFilesAsync()
    {
        foreach (var path in Paths)
        {
            tree.Add(path, "content of " + path);
        }

        await ledger.UpsertFilesAsync(tree.Files.Select(file => new FileRecord(file.Path, Languages.FromPath(file.Path), file.KnownHash!, file.Size, file.Mtime,
            "2026-09-28T10:00:00.0000000Z", "2026-09-28T10:00:00.0000000Z", null, null, null, null, null, null)).ToList(), CancellationToken.None);
    }

    private async Task<McpTools> ToolsAsync(bool references = true, Func<CancellationToken, Task<string>>? refresh = null)
    {
        await StoreAsync(Map(references));
        await StoreFilesAsync();
        return new McpTools(ledger, tree, hasher, refresh);
    }

    private static Task<McpToolResult> CallAsync(McpTools tools, string name, string arguments) =>
        tools.CallAsync(name, JsonNode.Parse(arguments)!.AsObject(), CancellationToken.None);

    private static JsonArray Array(McpToolResult result, string name) => result.Structured[name]!.AsArray();

    [Fact]
    public void List_NamesEveryTool_WithAnObjectSchema_AndADescription()
    {
        Assert.Equal(
            ["find_symbol", "references", "callers", "callees", "call_path", "impact", "http_links", "entry_points", "duplicates"],
            McpTools.List.Select(tool => tool.Name));
        Assert.All(McpTools.List, tool =>
        {
            Assert.Equal("object", JsonNode.Parse(tool.InputSchema)!["type"]!.GetValue<string>());
            Assert.True(tool.Description.Length > 60, tool.Name);
        });
        Assert.Equal(["symbol"], JsonNode.Parse(McpTools.List.Single(tool => tool.Name == "references").InputSchema)!["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task NoMap_ReturnsAnError_ThatSaysToScan()
    {
        var tools = new McpTools(ledger, tree, hasher);

        var result = await CallAsync(tools, "find_symbol", """{"query":"Load"}""");

        Assert.True(result.IsError);
        Assert.Contains("run codemuster scan", result.Text);
    }

    [Fact]
    public async Task FindSymbol_MatchesNamesDeclarationsAndPathFragments_AndFiltersByKind()
    {
        var tools = await ToolsAsync();

        var load = await CallAsync(tools, "find_symbol", """{"query":"Load"}""");
        var total = await CallAsync(tools, "find_symbol", """{"query":"Order.Total"}""");
        var byPath = await CallAsync(tools, "find_symbol", """{"query":"OrderRepository.cs"}""");
        var classes = await CallAsync(tools, "find_symbol", """{"query":"Order","kind":"class"}""");

        Assert.False(load.IsError);
        Assert.Equal([Audit, Service], Array(load, "results").Select(r => r!["id"]!.GetValue<string>()));
        var found = Array(total, "results").Single()!;
        Assert.Equal(Total, found["id"]!.GetValue<string>());
        Assert.Equal("property", found["kind"]!.GetValue<string>());
        Assert.Equal(ModelPath, found["path"]!.GetValue<string>());
        Assert.Equal(5, found["line"]!.GetValue<int>());
        Assert.Equal("Shop.Models.Order", found["container"]!.GetValue<string>());
        Assert.Equal("public decimal Total { get; set; }", found["signature"]!.GetValue<string>());
        Assert.Contains($"Order.Total  property  {ModelPath}:5", total.Text);
        Assert.Equal([Repository], Array(byPath, "results").Select(r => r!["id"]!.GetValue<string>()));
        Assert.Equal([Order], Array(classes, "results").Select(r => r!["id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task FindSymbol_StatesTheCap()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "find_symbol", """{"query":"Load","limit":1}""");

        Assert.Single(Array(result, "results"));
        Assert.Equal(1, result.Structured["truncated"]!.GetValue<int>());
        Assert.Contains("1 more not shown", result.Text);
    }

    [Fact]
    public async Task References_ListEveryKind_WithPositionAndContainer()
    {
        var tools = await ToolsAsync();

        var total = await CallAsync(tools, "references", """{"symbol":"Order.Total"}""");
        var order = await CallAsync(tools, "references", $$"""{"symbol":"{{Order}}"}""");
        var repository = await CallAsync(tools, "references", """{"symbol":"OrderRepository.Find"}""");
        var writes = await CallAsync(tools, "references", """{"symbol":"Order.Total","kind":"write"}""");

        Assert.Equal(
            [$"write {RepositoryPath}:22:13 OrderRepository.Find {Repository}", $"read {ServicePath}:15:20 OrderService.Load {Service}"],
            Array(total, "references").Select(r => $"{r!["kind"]} {r["path"]}:{r["line"]}:{r["column"]} {r["fromName"]} {r["from"]}"));
        Assert.Equal(["type"], Array(order, "references").Select(r => r!["kind"]!.GetValue<string>()));
        Assert.Equal([AuditPath, ServicePath], Array(repository, "references").Select(r => r!["path"]!.GetValue<string>()));
        Assert.Equal(["write"], Array(writes, "references").Select(r => r!["kind"]!.GetValue<string>()));
        Assert.Contains($"read  {ServicePath}:15:20  in OrderService.Load", total.Text);
    }

    [Fact]
    public async Task MapReferences_PrintsWhatTheReferencesToolAnswers_AsTextOrJson()
    {
        var tools = await ToolsAsync();

        var text = await tools.ReferencesAsync("Order.Total", null, MapFormat.Text, CancellationToken.None);
        var json = await tools.ReferencesAsync("Order.Total", "write", MapFormat.Json, CancellationToken.None);

        Assert.Equal((0, ""), (text.ExitCode, text.Error));
        Assert.StartsWith($"2 references to Order.Total ({Total})\n", text.Output);
        Assert.Contains($"  read  {ServicePath}:15:20  in OrderService.Load\n", text.Output);
        Assert.Contains($"  write  {RepositoryPath}:22:13  in OrderRepository.Find\n", text.Output);
        var parsed = JsonNode.Parse(json.Output)!;
        Assert.Equal(["write"], parsed["references"]!.AsArray().Select(r => r!["kind"]!.GetValue<string>()));
        Assert.Equal(Total, parsed["symbol"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task MapReferences_ExitsTwo_ForAnUnknownOrAmbiguousSymbol_OrNoMap()
    {
        var none = await new McpTools(new FakeLedger(), tree, hasher).ReferencesAsync("Order.Total", null, MapFormat.Text, CancellationToken.None);
        var tools = await ToolsAsync();

        var missing = await tools.ReferencesAsync("Nope", null, MapFormat.Text, CancellationToken.None);
        var ambiguous = await tools.ReferencesAsync("Load", null, MapFormat.Text, CancellationToken.None);

        Assert.Equal((2, ""), (none.ExitCode, none.Output));
        Assert.Contains("no code map yet", none.Error);
        Assert.Equal((2, ""), (missing.ExitCode, missing.Output));
        Assert.Contains("no symbol or declaration matches \"Nope\"", missing.Error);
        Assert.Equal(2, ambiguous.ExitCode);
        Assert.Contains(Service, ambiguous.Error);
        Assert.Contains(Audit, ambiguous.Error);
    }

    [Fact]
    public async Task References_ToAnEndpoint_IncludeTheUiCallsThatReachIt()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "references", """{"symbol":"OrdersController.Get"}""");

        var http = Array(result, "references").Single(r => r!["kind"]!.GetValue<string>() == "http")!;
        Assert.Equal(Client, http["from"]!.GetValue<string>());
        Assert.Equal(ClientPath, http["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task References_WhenTheMapHasNone_SayWhy()
    {
        var tools = await ToolsAsync(references: false);

        var result = await CallAsync(tools, "references", """{"symbol":"OrderRepository.Find"}""");

        Assert.False(result.IsError);
        Assert.Empty(Array(result, "references"));
        Assert.Contains("references are unavailable", result.Text);
        Assert.Contains("schema 9", result.Text);
    }

    [Fact]
    public async Task References_ForALanguageWithoutReferences_SayItWasNotMapped()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "references", """{"symbol":"loadOrder"}""");

        Assert.Contains("no typescript references were recorded", result.Text);
    }

    [Fact]
    public async Task AnAmbiguousSymbol_IsAnError_ThatListsTheCandidates()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "references", """{"symbol":"Load"}""");

        Assert.True(result.IsError);
        Assert.Equal([Audit, Service], Array(result, "candidates").Select(c => c!["id"]!.GetValue<string>()));
        Assert.Contains("matches 2 symbols", result.Text);
        var missing = await CallAsync(tools, "callers", """{"symbol":"Nothing"}""");
        Assert.True(missing.IsError);
        Assert.Contains("no symbol or declaration matches \"Nothing\"", missing.Text);
    }

    [Fact]
    public async Task Callers_WalkTheRequestedDepth_WithCallSites()
    {
        var tools = await ToolsAsync();

        var direct = await CallAsync(tools, "callers", """{"symbol":"OrderRepository.Find"}""");
        var deeper = await CallAsync(tools, "callers", """{"symbol":"OrderRepository.Find","depth":2}""");

        Assert.Equal(
            [$"1 call {Service} 14:16", $"1 call {Audit} 7:9"],
            Array(direct, "edges").Select(e => $"{e!["depth"]} {e["kind"]} {e["from"]} {string.Join(",", e["callSites"]!.AsArray().Select(s => $"{s!["line"]}:{s["column"]}"))}"));
        Assert.Contains($"<- call  OrderService.Load  {ServicePath}:12  at {ServicePath}:14:16", direct.Text);
        Assert.Contains(Array(deeper, "edges"), e => e!["from"]!.GetValue<string>() == Controller && e["kind"]!.GetValue<string>() == "bound" && e["depth"]!.GetValue<int>() == 2);
    }

    [Fact]
    public async Task Callees_ClampTheDepth_AndSayItWasClamped()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "callees", """{"symbol":"OrdersPage","depth":9}""");

        Assert.Equal(6, result.Structured["depth"]!.GetValue<int>());
        Assert.Contains("depth 9 is above the maximum of 6", result.Text);
        Assert.Equal([Client, Controller, Service, Repository], Array(result, "edges").Select(e => e!["to"]!.GetValue<string>()));
        Assert.Equal("http", Array(result, "edges")[1]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task CallPath_FromEveryEntryPoint_OrFromAGivenSymbol()
    {
        var tools = await ToolsAsync();

        var entries = await CallAsync(tools, "call_path", """{"to":"OrderRepository.Find"}""");
        var given = await CallAsync(tools, "call_path", """{"from":"loadOrder","to":"OrderRepository.Find"}""");

        Assert.Equal(
            [$"GET /orders/{{id}}: {Controller} {Service} {Repository}", $"/orders: {Page} {Client} {Controller} {Service} {Repository}"],
            Array(entries, "paths").Select(p => $"{p!["entry"]}: {string.Join(" ", p["steps"]!.AsArray().Select(s => s!["id"]!.GetValue<string>()))}"));
        Assert.Contains("GET /orders/{id}: OrdersController.Get -bound-> OrderService.Load -call-> OrderRepository.Find", entries.Text);
        Assert.Equal([Client, Controller, Service, Repository], Array(given, "paths").Single()!["steps"]!.AsArray().Select(s => s!["id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Impact_ListsCallersEntryPointsPagesCallees_AndReferenceUsers()
    {
        var tools = await ToolsAsync();

        var service = await CallAsync(tools, "impact", """{"symbol":"OrderService.Load"}""");
        var total = await CallAsync(tools, "impact", """{"symbol":"Order.Total"}""");

        Assert.Equal([Controller, Client, Page], Array(service, "callers").Select(c => c!["id"]!.GetValue<string>()));
        Assert.Equal(["GET /orders/{id} http", "/orders page"], Array(service, "entryPoints").Select(e => $"{e!["display"]} {e["kind"]}"));
        Assert.Equal([Repository], Array(service, "callees").Select(c => c!["id"]!.GetValue<string>()));
        Assert.Contains("entry points: GET /orders/{id} (http)", service.Text);
        Assert.Contains("pages: /orders", service.Text);

        Assert.Equal([Service], total.Structured["references"]!["read"]!.AsArray().Select(r => r!["from"]!.GetValue<string>()));
        Assert.Equal([Repository], total.Structured["references"]!["write"]!.AsArray().Select(r => r!["from"]!.GetValue<string>()));
        Assert.Equal(["GET /orders/{id} http", "/orders page"], Array(total, "entryPoints").Select(e => $"{e!["display"]} {e["kind"]}"));
        Assert.Contains($"writers: OrderRepository.Find ({RepositoryPath}:22)", total.Text);
    }

    [Fact]
    public async Task HttpLinks_ForAnEndpoint_ForAUiFunction_AndUnmatchedCalls()
    {
        var tools = await ToolsAsync();

        var endpoint = await CallAsync(tools, "http_links", """{"endpoint":"GET /orders/{id}"}""");
        var client = await CallAsync(tools, "http_links", """{"symbol":"loadOrder"}""");
        var all = await CallAsync(tools, "http_links", "{}");

        var link = Array(endpoint, "links").Single()!;
        Assert.Equal("GET /orders/{id}", link["endpoint"]!.GetValue<string>());
        Assert.Equal(Controller, link["handler"]!.GetValue<string>());
        Assert.Equal(Client, link["from"]!.GetValue<string>());
        Assert.Equal("GET /orders/{id}", Array(client, "links").Single()!["endpoint"]!.GetValue<string>());
        var unmatched = Array(client, "unmatched").Single()!;
        Assert.Equal($"{ClientPath}:9", $"{unmatched["path"]}:{unmatched["line"]}");
        Assert.Contains("POST /orders/{p} matches no endpoint", client.Text);
        Assert.Single(Array(all, "links"));
        Assert.Single(Array(all, "unmatched"));
    }

    [Fact]
    public async Task EntryPoints_FilterByKind()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "entry_points", """{"kind":"page"}""");

        Assert.Equal(["/orders"], Array(result, "entryPoints").Select(e => e!["display"]!.GetValue<string>()));
        Assert.Contains($"/orders (page)  OrdersPage  {PagePath}:1", result.Text);
    }

    [Fact]
    public async Task Duplicates_GroupByNormalizedBody()
    {
        var tools = await ToolsAsync();

        var all = await CallAsync(tools, "duplicates", "{}");
        var one = await CallAsync(tools, "duplicates", """{"symbol":"AuditService.Load"}""");
        var none = await CallAsync(tools, "duplicates", """{"symbol":"OrderRepository.Find"}""");

        Assert.Equal([Audit, Service], Array(all, "groups").Single()!["copies"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()));
        Assert.Single(Array(one, "groups"));
        Assert.Empty(Array(none, "groups"));
        Assert.Contains("no copies", none.Text);
    }

    [Fact]
    public async Task Results_NameTheCitedFilesChangedSinceTheScan()
    {
        var tools = await ToolsAsync();
        tree.Add(ServicePath, "edited after the scan");

        var result = await CallAsync(tools, "callers", """{"symbol":"OrderRepository.Find"}""");
        var unrelated = await CallAsync(tools, "find_symbol", """{"query":"OrdersPage"}""");

        Assert.Equal([ServicePath], Array(result, "stale").Select(p => p!.GetValue<string>()));
        Assert.Contains($"stale: {ServicePath} changed since the scan", result.Text);
        Assert.Contains("run codemuster scan", result.Text);
        Assert.Empty(Array(unrelated, "stale"));
    }

    [Fact]
    public async Task ANewScan_IsPickedUp_BeforeTheNextCall()
    {
        var tools = await ToolsAsync();
        Assert.True((await CallAsync(tools, "find_symbol", """{"query":"Checkout"}""")).Structured["results"]!.AsArray().Count == 0);
        var reads = ledger.CodeMapReads;

        await CallAsync(tools, "find_symbol", """{"query":"Load"}""");
        Assert.Equal(reads, ledger.CodeMapReads);

        var map = Map();
        await StoreAsync(map with { Symbols = [.. map.Symbols, Method("M:Shop.Checkout.Pay", ServicePath, 40, "public void Pay()")] }, "2026-09-28T11:00:00.0000000Z");
        var result = await CallAsync(tools, "find_symbol", """{"query":"Checkout"}""");

        Assert.Single(Array(result, "results"));
        Assert.Equal("2026-09-28T11:00:00.0000000Z", result.Structured["map"]!["scannedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refresh_RunsOnlyWhenTheMapIsStale_AndItsNoteIsShown()
    {
        var refreshes = 0;
        var tools = await ToolsAsync(refresh: _ =>
        {
            refreshes++;
            return Task.FromResult("refresh skipped: another CodeMuster command is using this repository");
        });

        await CallAsync(tools, "entry_points", "{}");
        Assert.Equal(0, refreshes);

        tree.Add(RepositoryPath, "edited after the scan");
        var result = await CallAsync(tools, "entry_points", "{}");

        Assert.Equal(1, refreshes);
        Assert.Contains("refresh skipped: another CodeMuster command is using this repository", result.Text);
    }

    [Fact]
    public async Task MissingArguments_AreToolErrors()
    {
        var tools = await ToolsAsync();

        var result = await CallAsync(tools, "references", "{}");

        Assert.True(result.IsError);
        Assert.Contains("symbol is required", result.Text);
    }
}
