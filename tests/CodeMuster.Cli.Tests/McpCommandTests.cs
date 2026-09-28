using System.Text.Json.Nodes;

namespace CodeMuster.Cli.Tests;

public class McpCommandTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    private static string Call(int id, string tool, string arguments) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}";

    private static IReadOnlyList<JsonObject> Replies(CliResult result)
    {
        // Every line on standard output must be one JSON-RPC message; anything else would break the client.
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Select(line => JsonNode.Parse(line)!.AsObject()).Select(reply =>
        {
            Assert.Equal("2.0", reply["jsonrpc"]!.GetValue<string>());
            return reply;
        }).ToList();
    }

    private static JsonObject ById(IReadOnlyList<JsonObject> replies, int id) => replies.Single(reply => reply["id"]?.GetValue<int>() == id);

    [Fact]
    public async Task Mcp_OverStandardInput_ListsTools_AndAnswersFromTheStoredMap()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);
        File.AppendAllText(Path.Combine(repo.Root, "src", "MixedRepo.Api", "Services", "QuoteService.cs"), "\n// edited after the scan\n");

        var result = await CliProcess.RunWithInputAsync(repo.Root, string.Join("\n",
            Initialize,
            Initialized,
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            Call(3, "callers", """{"symbol":"QuoteRepository.FindForTenant"}"""),
            Call(4, "find_symbol", """{"query":"ListQuotes"}"""),
            Call(5, "http_links", "{}"),
            """{"jsonrpc":"2.0","id":6,"method":"no/such"}""",
            "not json") + "\n", "mcp");

        Assert.Equal(0, result.ExitCode);
        var replies = Replies(result);
        Assert.Equal(7, replies.Count);
        Assert.Equal("2025-06-18", ById(replies, 1)["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal(
            ["find_symbol", "references", "callers", "callees", "call_path", "impact", "http_links", "entry_points", "duplicates"],
            ById(replies, 2)["result"]!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()));

        var callers = ById(replies, 3)["result"]!;
        Assert.False(callers["isError"]!.GetValue<bool>());
        Assert.Contains("<- call  QuoteService.GetQuote  src/MixedRepo.Api/Services/QuoteService.cs:13", callers["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(["src/MixedRepo.Api/Services/QuoteService.cs"], callers["structuredContent"]!["stale"]!.AsArray().Select(path => path!.GetValue<string>()));

        var found = ById(replies, 4)["result"]!["structuredContent"]!["results"]!.AsArray();
        Assert.Equal(4, found.Count);
        Assert.Contains(found, item => item!.ToJsonString().Contains("IQuoteService.ListQuotes", StringComparison.Ordinal));
        Assert.NotEmpty(ById(replies, 5)["result"]!["structuredContent"]!["links"]!.AsArray());
        Assert.Equal(-32601, ById(replies, 6)["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32700, replies[^1]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Mcp_WithRefresh_ScansAStaleMapFirst_UnlessAnotherCommandHoldsTheLock()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);
        File.AppendAllText(Path.Combine(repo.Root, "web", "lib", "api.ts"), "\nexport function retireQuoteForTest(id: number) {\n  return id;\n}\n");
        var request = Call(1, "find_symbol", """{"query":"retireQuoteForTest"}""") + "\n";

        CliResult locked;
        var common = repo.Git("rev-parse", "--path-format=absolute", "--git-common-dir").Trim();
        using (new FileStream(Path.Combine(common, "codemuster-coordinator.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await CliProcess.RunWithInputAsync(repo.Root, request, "mcp", "--refresh");
        }

        var plain = await CliProcess.RunWithInputAsync(repo.Root, request, "mcp");
        var refreshed = await CliProcess.RunWithInputAsync(repo.Root, request, "mcp", "--refresh");

        var skipped = Replies(locked).Single()["result"]!;
        Assert.Contains("refresh skipped: another CodeMuster command is using this repository", skipped["content"]![0]!["text"]!.GetValue<string>());
        Assert.Empty(skipped["structuredContent"]!["results"]!.AsArray());
        Assert.Empty(Replies(plain).Single()["result"]!["structuredContent"]!["results"]!.AsArray());
        var found = Replies(refreshed).Single()["result"]!;
        Assert.Contains("refreshed: scanned", found["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("web/lib/api.ts", found["structuredContent"]!["results"]![0]!["path"]!.GetValue<string>());
        Assert.Empty(found["structuredContent"]!["stale"]!.AsArray());
    }

    [Fact]
    public async Task Mcp_WithoutAMap_AnswersWithAToolErrorThatSaysToScan()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var before = await CliProcess.RunWithInputAsync(repo.Root, Call(1, "find_symbol", """{"query":"ListQuotes"}""") + "\n", "mcp");
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".codemuster")));
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        var after = await CliProcess.RunWithInputAsync(repo.Root, Call(1, "find_symbol", """{"query":"ListQuotes"}""") + "\n", "mcp");

        Assert.Equal(0, before.ExitCode);
        var notSetUp = Replies(before).Single()["result"]!;
        Assert.True(notSetUp["isError"]!.GetValue<bool>());
        Assert.Contains("codemuster init", notSetUp["content"]![0]!["text"]!.GetValue<string>());

        var noMap = Replies(after).Single()["result"]!;
        Assert.True(noMap["isError"]!.GetValue<bool>());
        Assert.Contains("run codemuster scan", noMap["content"]![0]!["text"]!.GetValue<string>());
    }
}
