using System.Text.Json.Nodes;
using CodeMuster.Application;

namespace CodeMuster.Cli.Tests;

public class McpServerTests
{
    private const string Modern = """{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}}""";

    private static async Task<(IReadOnlyList<JsonObject> Replies, string Stdout, string Log)> ServeAsync(Func<string, JsonObject?, CancellationToken, Task<McpToolResult>> call, params string[] lines)
    {
        var output = new StringWriter();
        var log = new StringWriter();
        await new McpServer(new StringReader(string.Join("\n", lines) + "\n"), output, log, "9.8.7", call).RunAsync(CancellationToken.None);
        var stdout = output.ToString();
        return (stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList(), stdout, log.ToString());
    }

    private static Task<(IReadOnlyList<JsonObject> Replies, string Stdout, string Log)> ServeAsync(params string[] lines) =>
        ServeAsync((name, args, _) => Task.FromResult(new McpToolResult($"answer from {name} for {args?["symbol"]}", new JsonObject { ["answer"] = 42 }, false)), lines);

    [Theory]
    [InlineData("2025-06-18", "2025-06-18")]
    [InlineData("2025-11-25", "2025-11-25")]
    [InlineData("2024-11-05", "2025-11-25")]
    public async Task Initialize_AcceptsASupportedVersion_ElseOffersTheLatest(string requested, string answered)
    {
        var (replies, _, _) = await ServeAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"" + requested + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}");

        var result = replies.Single()["result"]!;
        Assert.Equal(1, replies[0]["id"]!.GetValue<int>());
        Assert.Equal("2.0", replies[0]["jsonrpc"]!.GetValue<string>());
        Assert.Equal(answered, result["protocolVersion"]!.GetValue<string>());
        Assert.Equal("codemuster", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("9.8.7", result["serverInfo"]!["version"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task Notifications_GetNoReply_AndPingGetsAnEmptyResult()
    {
        var (replies, _, _) = await ServeAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","method":"notifications/unknown"}""",
            """{"jsonrpc":"2.0","id":"p","method":"ping"}""");

        Assert.Equal("p", replies.Single()["id"]!.GetValue<string>());
        Assert.Empty(replies[0]["result"]!.AsObject());
    }

    [Fact]
    public async Task ToolsList_DescribesEveryToolAsReadOnly()
    {
        var (replies, _, _) = await ServeAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        var tools = replies.Single()["result"]!["tools"]!.AsArray();
        Assert.Equal(McpTools.List.Select(tool => tool.Name), tools.Select(tool => tool!["name"]!.GetValue<string>()));
        Assert.All(tools, tool =>
        {
            Assert.Equal("object", tool!["inputSchema"]!["type"]!.GetValue<string>());
            Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
            Assert.False(string.IsNullOrEmpty(tool["description"]!.GetValue<string>()));
        });
    }

    [Fact]
    public async Task ToolsCall_ReturnsTextAndStructuredContent()
    {
        var (replies, _, _) = await ServeAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"callers","arguments":{"symbol":"Login"}}}""");

        var result = replies.Single()["result"]!;
        Assert.Equal("text", result["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("answer from callers for Login", result["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(42, result["structuredContent"]!["answer"]!.GetValue<int>());
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Errors_UseJsonRpcCodes_AndTheServerKeepsReading()
    {
        var (replies, _, _) = await ServeAsync(
            "{not json",
            """{"jsonrpc":"2.0","id":4,"method":"resources/list"}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"no_such_tool","arguments":{}}}""",
            """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{}}""",
            "[]",
            """{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        Assert.Equal([-32700, -32601, -32602, -32602, -32600], replies.Take(5).Select(reply => reply["error"]!["code"]!.GetValue<int>()));
        Assert.Null(replies[0]["id"]);
        Assert.True(replies[0].ContainsKey("id"));
        Assert.Equal(4, replies[1]["id"]!.GetValue<int>());
        Assert.Equal(7, replies[5]["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task AFailingTool_IsAnInternalError_LoggedToStandardError()
    {
        var (replies, _, log) = await ServeAsync(
            (_, _, _) => throw new InvalidOperationException("ledger locked"),
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"impact","arguments":{"symbol":"x"}}}""");

        Assert.Equal(-32603, replies.Single()["error"]!["code"]!.GetValue<int>());
        Assert.Contains("ledger locked", log);
    }

    [Fact]
    public async Task ModernRequests_CarryTheirVersionInMeta()
    {
        var (replies, _, _) = await ServeAsync(
            """{"jsonrpc":"2.0","id":"d","method":"server/discover","params":{"_meta":""" + Modern + "}}",
            """{"jsonrpc":"2.0","id":9,"method":"tools/list","params":{"_meta":""" + Modern + "}}",
            """{"jsonrpc":"2.0","id":10,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"1900-01-01","io.modelcontextprotocol/clientCapabilities":{}}}}""",
            """{"jsonrpc":"2.0","id":11,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28"}}}""");

        var discover = replies[0]["result"]!;
        Assert.Equal("complete", discover["resultType"]!.GetValue<string>());
        Assert.Contains("2026-07-28", discover["supportedVersions"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Contains("2025-06-18", discover["supportedVersions"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal("codemuster", discover["_meta"]!["io.modelcontextprotocol/serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(discover["capabilities"]!["tools"]);
        Assert.Equal("complete", replies[1]["result"]!["resultType"]!.GetValue<string>());
        Assert.Equal(-32022, replies[2]["error"]!["code"]!.GetValue<int>());
        Assert.Equal("1900-01-01", replies[2]["error"]!["data"]!["requested"]!.GetValue<string>());
        Assert.Equal(-32602, replies[3]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task EveryReply_IsOneLineOfJson()
    {
        var (replies, stdout, _) = await ServeAsync(
            (_, _, _) => Task.FromResult(new McpToolResult("line one\nline two", new JsonObject { ["text"] = "a\nb" }, false)),
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"find_symbol","arguments":{"query":"x"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        Assert.Equal(2, replies.Count);
        Assert.Equal(2, stdout.Count(c => c == '\n'));
        Assert.DoesNotContain('\r', stdout);
    }
}
