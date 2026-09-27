using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class CodeMapQueryTests
{
    private const string Get = "M:App.Web.ItemsController.Get(System.Int32)";
    private const string Load = "M:App.Services.ItemService.Load(System.Int32)";
    private const string OtherLoad = "M:App.Services.CachedItemService.Load(System.Int32)";
    private const string Find = "M:App.Data.ItemRepository.Find(System.Int32)";
    private const string Again = "M:App.Data.ItemRepository.Again";
    private const string Page = "web/app/items/page.tsx#ItemsPage";
    private const string Missing = "M:App.Gone.Nowhere";

    private static Symbol Method(string id, string path, int line, string signature) =>
        new(id, path, new LineRange(line, line + 3), "method", signature, "hash-" + line);

    private static StoredCodeMap Sample(IReadOnlyList<string>? failed = null, IReadOnlyList<string>? diagnostics = null) => new(
        "abcdef1234567890",
        "2026-09-27T10:00:00.0000000Z",
        new CodeMap(
            [
                Method(Get, "src/Web/ItemsController.cs", 9, "public sealed class ItemsController\n[HttpGet(\"items\")] public Item Get(int id)"),
                Method(Load, "src/Services/ItemService.cs", 12, "public sealed class ItemService : IItemService\npublic Item Load(int id)"),
                Method(OtherLoad, "src/Services/CachedItemService.cs", 5, "public sealed class CachedItemService : IItemService\npublic Item Load(int id)"),
                Method(Find, "src/Data/ItemRepository.cs", 20, "public sealed class ItemRepository\npublic Item Find(int id)"),
                Method(Again, "src/Data/ItemRepository.cs", 30, "public sealed class ItemRepository\npublic void Again()"),
                Method(Find, "src/Data/ItemRepository.cs", 20, "public sealed class ItemRepository\npublic Item Find(int id)"),
                new Symbol(Page, "web/app/items/page.tsx", new LineRange(1, 9), "function", "export default function ItemsPage()", "hash-page"),
            ],
            [
                new Edge(Get, Load, EdgeKind.Bound),
                new Edge(Get, OtherLoad, EdgeKind.Implements),
                new Edge(Load, Find, EdgeKind.Call),
                new Edge(Load, Find, EdgeKind.Call),
                new Edge(Find, Again, EdgeKind.Call),
                new Edge(Again, Find, EdgeKind.Overrides),
                new Edge(OtherLoad, Missing, EdgeKind.Call),
            ],
            [
                new EntryPoint(Get, "http", "GET /items"),
                new EntryPoint(Page, "page", "/items"),
            ],
            new ResolutionStats(7, 1, ["Nowhere"]),
            diagnostics ?? []),
        failed is null ? [Languages.CSharp, Languages.TypeScript] : [Languages.TypeScript],
        failed ?? []);

    private static async Task<MapResult> RunAsync(StoredCodeMap? map, MapRequest request)
    {
        var ledger = new FakeLedger();
        if (map is not null)
        {
            await ledger.ReplaceCodeMapAsync(map, CancellationToken.None);
        }

        return await new CodeMapQuery(ledger).RunAsync(request, CancellationToken.None);
    }

    private static MapRequest Walk(string subcommand, string target, int? depth = null, MapFormat format = MapFormat.Text) =>
        new(subcommand, target, depth, format, Page: false);

    private static string[] Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    [Theory]
    [InlineData(null, null)]
    [InlineData("callers", "ItemRepository.Find")]
    [InlineData("flow", "GET /items")]
    public async Task NoStoredMap_Exits2_AndSaysToScan(string? subcommand, string? target)
    {
        var result = await RunAsync(null, new MapRequest(subcommand, target, null, MapFormat.Text, Page: false));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Equal("no code map yet; run codemuster scan", result.Error);
    }

    [Fact]
    public async Task Flow_MatchesAPageRoute_WithoutItsLeadingSlash()
    {
        var result = await RunAsync(Sample(), Walk("flow", "items"));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("flow /items (page) from ItemsPage", Lines(result.Output)[0]);
    }

    [Fact]
    public async Task Flow_ExplainsARouteGitBashRewroteIntoAWindowsPath()
    {
        var result = await RunAsync(Sample(), Walk("flow", "C:/Program Files/Git/items"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Git Bash rewrote /items into \"C:/Program Files/Git/items\"; write it as items", result.Error);
    }

    [Fact]
    public async Task Summary_ListsTheUiToApiResults_SeparatelyFromPartialMapWarnings()
    {
        var result = await RunAsync(Sample(diagnostics: ["http: web/lib/api.ts:14 GET /customers matches no endpoint", "http: GET /items/{id} is not called from the mapped UI"]), new MapRequest(null, null, null, MapFormat.Text, Page: false));

        var lines = Lines(result.Output);
        Assert.Contains("ui to api:", lines);
        Assert.Contains("  web/lib/api.ts:14 GET /customers matches no endpoint", lines);
        Assert.Contains("  GET /items/{id} is not called from the mapped UI", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("warning: partial map", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callees_ListsDirectCallees_WithKindPathLineAndSignature_AtDepthOne()
    {
        var result = await RunAsync(Sample(), Walk("callees", "ItemService.Load"));

        Assert.Equal(0, result.ExitCode);
        var lines = Lines(result.Output);
        Assert.Equal("callees of ItemService.Load (M:App.Services.ItemService.Load(System.Int32)), depth 1", lines[0]);
        Assert.Equal("ItemService.Load  src/Services/ItemService.cs:12  public Item Load(int id)", lines[1]);
        Assert.Equal("  -> call  ItemRepository.Find  src/Data/ItemRepository.cs:20  public Item Find(int id)", lines[2]);
        Assert.DoesNotContain("ItemRepository.Again", result.Output);
        Assert.Contains("truncated: 1 more nodes; use --depth or a narrower start", result.Output);
    }

    [Fact]
    public async Task Callers_ListsDirectCallers_WithTheirEdgeKinds()
    {
        var result = await RunAsync(Sample(), Walk("callers", "ItemRepository.Find"));

        Assert.Equal(0, result.ExitCode);
        var lines = Lines(result.Output);
        Assert.Equal("callers of ItemRepository.Find (M:App.Data.ItemRepository.Find(System.Int32)), depth 1", lines[0]);
        Assert.Contains("  <- overrides  ItemRepository.Again  src/Data/ItemRepository.cs:30  public void Again()", lines);
        Assert.Contains("  <- call  ItemService.Load  src/Services/ItemService.cs:12  public Item Load(int id)", lines);
        Assert.DoesNotContain("ItemsController.Get", result.Output);
    }

    [Fact]
    public async Task Callers_WithDepth_WalksFurther_AsATree()
    {
        var result = await RunAsync(Sample(), Walk("callers", "ItemRepository.Find", depth: 3));

        var lines = Lines(result.Output);
        var load = Array.IndexOf(lines, "  <- call  ItemService.Load  src/Services/ItemService.cs:12  public Item Load(int id)");
        Assert.True(load > 0, result.Output);
        Assert.Equal("    <- bound  ItemsController.Get  src/Web/ItemsController.cs:9  [HttpGet(\"items\")] public Item Get(int id)", lines[load + 1]);
        Assert.DoesNotContain("truncated", result.Output);
    }

    [Fact]
    public async Task Flow_MatchesTheEntryPointCaseInsensitively_AndWalksTheWholeTree()
    {
        var result = await RunAsync(Sample(), Walk("flow", "get /ITEMS"));

        Assert.Equal(0, result.ExitCode);
        var lines = Lines(result.Output);
        Assert.Equal("flow GET /items (http) from ItemsController.Get (M:App.Web.ItemsController.Get(System.Int32)), depth 6", lines[0]);
        Assert.Contains("  -> bound  ItemService.Load  src/Services/ItemService.cs:12  public Item Load(int id)", lines);
        Assert.Contains("    -> call  ItemRepository.Find  src/Data/ItemRepository.cs:20  public Item Find(int id)", lines);
        Assert.Contains("  -> implements  CachedItemService.Load  src/Services/CachedItemService.cs:5  public Item Load(int id)", lines);
        Assert.Contains("    -> call  M:App.Gone.Nowhere  (not in the map)", lines);
    }

    [Fact]
    public async Task Flow_BySymbolId_Works()
    {
        var result = await RunAsync(Sample(), Walk("flow", Get));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("flow GET /items (http) from ItemsController.Get", result.Output);
    }

    [Fact]
    public async Task Flow_ThroughACycle_Terminates_AndShowsTheRepeatOnce()
    {
        var result = await RunAsync(Sample(), Walk("flow", "GET /items", depth: 50, MapFormat.Json));

        using var json = JsonDocument.Parse(result.Output);
        var ids = json.RootElement.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetString()).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        Assert.Contains(Again, ids);

        var text = await RunAsync(Sample(), Walk("flow", "GET /items", depth: 50));
        Assert.Contains("        -> overrides  ItemRepository.Find  src/Data/ItemRepository.cs:20  (see above)", Lines(text.Output));
    }

    [Fact]
    public async Task AmbiguousName_Exits2_AndListsTheMatches()
    {
        var result = await RunAsync(Sample(), Walk("callees", "Load"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.Output);
        var lines = Lines(result.Error);
        Assert.Equal("\"Load\" matches 2 symbols; use a more exact name or one of these ids:", lines[0]);
        Assert.Contains("  CachedItemService.Load  src/Services/CachedItemService.cs:5  M:App.Services.CachedItemService.Load(System.Int32)", lines);
        Assert.Contains("  ItemService.Load  src/Services/ItemService.cs:12  M:App.Services.ItemService.Load(System.Int32)", lines);
    }

    [Theory]
    [InlineData(Load)]
    [InlineData("App.Services.ItemService.Load")]
    [InlineData("itemservice.load")]
    public async Task ExactIdOrQualifiedName_PicksOneSymbol(string query)
    {
        var result = await RunAsync(Sample(), Walk("callees", query));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("callees of ItemService.Load ", result.Output);
    }

    [Fact]
    public async Task PartialName_MatchingOneSymbol_IsUsed()
    {
        var result = await RunAsync(Sample(), Walk("callers", "ItemsPa"));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("callers of ItemsPage (web/app/items/page.tsx#ItemsPage), depth 1", result.Output);
        Assert.Contains("(no callers)", result.Output);
    }

    [Fact]
    public async Task UnknownSymbolOrEntryPoint_Exits2()
    {
        var symbol = await RunAsync(Sample(), Walk("callers", "Nope"));
        var flow = await RunAsync(Sample(), Walk("flow", "POST /nope"));

        Assert.Equal(2, symbol.ExitCode);
        Assert.Equal("no symbol matches \"Nope\"", Lines(symbol.Error)[0]);
        Assert.Equal(2, flow.ExitCode);
        Assert.Equal(["no entry point matches \"POST /nope\"; entry points:", "  GET /items (http)  ItemsController.Get", "  /items (page)  ItemsPage"], Lines(flow.Error));
    }

    private static StoredCodeMap Star(int children)
    {
        var symbols = new List<Symbol> { Method("M:Big.Root.Go", "src/Root.cs", 1, "void Go()") };
        var edges = new List<Edge>();
        for (var i = 0; i < children; i++)
        {
            var id = $"M:Big.Leaf{i:D3}.Run";
            symbols.Add(Method(id, $"src/Leaf{i:D3}.cs", 1, "void Run()"));
            edges.Add(new Edge("M:Big.Root.Go", id, EdgeKind.Call));
        }

        return new StoredCodeMap("c0ffee", "2026-09-27T10:00:00.0000000Z", new CodeMap(symbols, edges, [new EntryPoint("M:Big.Root.Go", "background", "Root")], new ResolutionStats(0, 0, []), []), [Languages.CSharp], []);
    }

    [Theory]
    [InlineData(MapFormat.Text, "truncated: 101 more nodes; use --depth or a narrower start")]
    [InlineData(MapFormat.Mermaid, "%% truncated: 101 more nodes; use --depth or a narrower start")]
    [InlineData(MapFormat.Json, "\"truncated\": 101")]
    public async Task Walks_StopAt300Nodes_AndSaySoInEveryFormat(MapFormat format, string notice)
    {
        var result = await RunAsync(Star(400), Walk("flow", "Root", format: format));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(notice, result.Output);
        if (format == MapFormat.Json)
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal(300, json.RootElement.GetProperty("nodes").GetArrayLength());
        }
    }

    [Fact]
    public async Task Mermaid_IsAFlowchart_WithStableIds_KindLabels_AndADistinctEntryPoint()
    {
        var result = await RunAsync(Sample(), Walk("flow", "GET /items", format: MapFormat.Mermaid));

        var lines = Lines(result.Output);
        Assert.Equal("flowchart TD", lines[0]);
        Assert.Contains("  entry([\"GET /items\"])", lines);
        Assert.Contains("  entry ==> n0", lines);
        Assert.Contains("  n0[\"ItemsController.Get\"]", lines);
        Assert.Contains("  n0 -->|bound| n1", lines);
        Assert.Contains("  n0 -->|implements| n2", lines);
        Assert.Contains("  class entry entry", lines);
        Assert.DoesNotContain(lines, line => line.Contains("truncated", StringComparison.Ordinal));
        Assert.Equal(result.Output, (await RunAsync(Sample(), Walk("flow", "GET /items", format: MapFormat.Mermaid))).Output);
    }

    [Fact]
    public async Task Mermaid_EscapesQuotesAngleBracketsAndHashes()
    {
        const string weird = "web/a.ts#Box<\"T\">.get#x";
        var map = new StoredCodeMap("c0ffee", "2026-09-27T10:00:00.0000000Z", new CodeMap(
            [new Symbol(weird, "web/a.ts", new LineRange(1, 2), "method", "get()", "h")],
            [], [], new ResolutionStats(0, 0, []), []), [Languages.TypeScript], []);

        var result = await RunAsync(map, Walk("callees", weird, format: MapFormat.Mermaid));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("  n0[\"Box#lt;#quot;T#quot;#gt;.get#35;x\"]", Lines(result.Output));
    }

    [Fact]
    public async Task Json_HasNodesEdgesAndTruncated_WithDeduplicatedSymbols()
    {
        var result = await RunAsync(Sample(), Walk("callees", "ItemService.Load", depth: 3, MapFormat.Json));

        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(0, root.GetProperty("truncated").GetInt32());
        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Equal([Load, Find, Again], nodes.Select(node => node.GetProperty("id").GetString()));
        var find = nodes[1];
        Assert.Equal("ItemRepository.Find", find.GetProperty("name").GetString());
        Assert.Equal("src/Data/ItemRepository.cs", find.GetProperty("path").GetString());
        Assert.Equal(20, find.GetProperty("line").GetInt32());
        Assert.Equal("method", find.GetProperty("kind").GetString());
        Assert.Equal("App.Data.ItemRepository", find.GetProperty("container").GetString());
        var edges = root.GetProperty("edges").EnumerateArray()
            .Select(edge => $"{edge.GetProperty("from").GetString()} {edge.GetProperty("kind").GetString()} {edge.GetProperty("to").GetString()}")
            .ToList();
        Assert.Equal([$"{Load} call {Find}", $"{Find} call {Again}", $"{Again} overrides {Find}"], edges);
    }

    [Fact]
    public async Task Summary_CountsSymbolsEdgesAndEntryPoints_AndHintsAtTheOtherForms()
    {
        var result = await RunAsync(Sample(), new MapRequest(null, null, null, MapFormat.Text, Page: false));

        Assert.Equal(0, result.ExitCode);
        var lines = Lines(result.Output);
        Assert.Equal("code map at abcdef1 (scanned 2026-09-27T10:00:00.0000000Z)", lines[0]);
        Assert.Contains("symbols: 6 (csharp 5, typescript 1)", lines);
        Assert.Contains("edges: 6 (bound 1, call 3, implements 1, overrides 1)", lines);
        Assert.Contains("entry points: 2 (http 1, page 1)", lines);
        Assert.Contains("  GET /items (http)  ItemsController.Get", lines);
        Assert.Contains("  codemuster map flow \"<entry point>\" [--depth N]", lines);
        Assert.Contains("  codemuster map callers <symbol> [--depth N]", lines);
        Assert.Contains("  codemuster map callees <symbol> [--depth N]", lines);
        Assert.Contains("  add --format mermaid|json, or --out map.html for an interactive page", lines);
        Assert.DoesNotContain("partial", result.Output);
    }

    [Fact]
    public async Task Summary_WarnsWhenTheMapIsPartial()
    {
        var result = await RunAsync(Sample(failed: [Languages.CSharp], diagnostics: ["csharp mapper failed: restore first"]), new MapRequest(null, null, null, MapFormat.Text, Page: false));

        Assert.Contains("warning: partial map; failed languages: csharp", Lines(result.Output));
        Assert.Contains("  csharp mapper failed: restore first", Lines(result.Output));
    }

    [Fact]
    public async Task Summary_AsJson_IsMachineReadable()
    {
        var result = await RunAsync(Sample(), new MapRequest(null, null, null, MapFormat.Json, Page: false));

        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal("abcdef1234567890", root.GetProperty("commit").GetString());
        Assert.Equal(6, root.GetProperty("symbols").GetInt32());
        Assert.Equal(3, root.GetProperty("edgesByKind").GetProperty("call").GetInt32());
        Assert.False(root.GetProperty("partial").GetBoolean());
        Assert.Equal("GET /items", root.GetProperty("entryPoints")[0].GetProperty("display").GetString());
    }

    [Fact]
    public async Task PageData_EmbedsTheWholeMapAndTheRequestedView()
    {
        var result = await RunAsync(Sample(), new MapRequest("flow", "GET /items", null, MapFormat.Text, Page: true));

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(6, root.GetProperty("symbols").GetArrayLength());
        Assert.Equal(6, root.GetProperty("edges").GetArrayLength());
        Assert.Equal(2, root.GetProperty("entryPoints").GetArrayLength());
        Assert.Equal(300, root.GetProperty("cap").GetInt32());
        var view = root.GetProperty("view");
        Assert.Equal("callees", view.GetProperty("direction").GetString());
        Assert.Equal(Get, view.GetProperty("root").GetString());
        Assert.Equal(6, view.GetProperty("depth").GetInt32());
        Assert.Equal("GET /items", view.GetProperty("entry").GetString());
        Assert.DoesNotContain("<", result.Output);
    }

    [Fact]
    public async Task PageData_EscapesHtmlSensitiveCharacters_SoItCannotCloseItsScriptElement()
    {
        var map = new StoredCodeMap("c0ffee", "2026-09-27T10:00:00.0000000Z", new CodeMap(
            [new Symbol("web/a.ts#get", "web/a.ts", new LineRange(1, 2), "function", "get(): Promise<\"</script><script>alert(1)</script>\"> & more", "h")],
            [], [], new ResolutionStats(0, 0, []), []), [Languages.TypeScript], []);

        var result = await RunAsync(map, new MapRequest(null, null, null, MapFormat.Text, Page: true));

        Assert.DoesNotContain("<", result.Output);
        Assert.DoesNotContain(">", result.Output);
        Assert.DoesNotContain("&", result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains("</script>", json.RootElement.GetProperty("symbols")[0].GetProperty("signature").GetString());
    }

    [Fact]
    public async Task PageData_WithoutASubcommand_HasNoView()
    {
        var result = await RunAsync(Sample(), new MapRequest(null, null, null, MapFormat.Text, Page: true));

        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("view").ValueKind);
    }
}
