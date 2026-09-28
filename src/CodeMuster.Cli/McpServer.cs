using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMuster.Application;

namespace CodeMuster.Cli;

/// <summary>
/// The MCP stdio transport for <c>codemuster mcp</c> (D73): one JSON-RPC 2.0 message per line in, one per line out, nothing else on the output.
/// Serves both eras: legacy clients open with <c>initialize</c>; modern clients (2026-07-28) put their version in every request's <c>_meta</c>.
/// </summary>
public sealed class McpServer(TextReader input, TextWriter output, TextWriter log, string version, Func<string, JsonObject?, CancellationToken, Task<McpToolResult>> call)
{
    /// <summary>The per-request metadata revision this server speaks.</summary>
    public const string ModernVersion = "2026-07-28";

    /// <summary>The initialize-handshake revisions this server speaks, newest first.</summary>
    public static IReadOnlyList<string> LegacyVersions { get; } = ["2025-11-25", "2025-06-18"];

    // Standard output is a pipe to the client, not HTML, so quotes and non-ASCII text are written as they are; newlines inside strings are still escaped.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";
    private const string ServerInfoKey = "io.modelcontextprotocol/serverInfo";

    private const string Instructions = "CodeMuster's map of this repository's code: symbols, declarations, references, call edges, entry points and UI-to-API links from the last codemuster scan. "
        + "Prefer these tools over text search for who calls or uses a symbol, how it is reached, and what a change affects. Results name files changed since the scan as stale.";

    private sealed class RpcException(int code, string message, JsonNode? data = null) : Exception(message)
    {
        public int Code { get; } = code;

        public JsonNode? Payload { get; } = data;
    }

    /// <summary>Answers messages until the input ends or <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (await HandleAsync(line, cancellationToken) is { } reply)
            {
                await output.WriteAsync(reply.ToJsonString(Compact) + "\n");
                await output.FlushAsync(cancellationToken);
            }
        }
    }

    private async Task<JsonObject?> HandleAsync(string line, CancellationToken cancellationToken)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error: each line must be one JSON-RPC message");
        }

        if (message is not JsonObject request || request["method"] is not JsonValue methodValue || !methodValue.TryGetValue<string>(out var method))
        {
            return Error(IdOf(message), -32600, "Invalid Request: expected one JSON-RPC request object with a method");
        }

        var id = request["id"]?.DeepClone();
        var isNotification = !request.ContainsKey("id");
        try
        {
            var parameters = request["params"] as JsonObject;
            var modern = Modern(parameters);
            var result = await DispatchAsync(method, parameters, modern, isNotification, cancellationToken);
            if (isNotification || result is null)
            {
                return null;
            }

            if (modern)
            {
                result["resultType"] = "complete";
                result["_meta"] = new JsonObject { [ServerInfoKey] = ServerInfo() };
            }

            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }
        catch (RpcException ex)
        {
            return isNotification ? null : Error(id, ex.Code, ex.Message, ex.Payload);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await log.WriteLineAsync($"codemuster mcp: {method} failed: {ex.Message}");
            return isNotification ? null : Error(id, -32603, "Internal error: " + ex.Message);
        }
    }

    private async Task<JsonObject?> DispatchAsync(string method, JsonObject? parameters, bool modern, bool isNotification, CancellationToken cancellationToken)
    {
        if (isNotification)
        {
            return null;
        }

        switch (method)
        {
            case "initialize":
                var requested = parameters?["protocolVersion"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
                return new JsonObject
                {
                    ["protocolVersion"] = requested is not null && LegacyVersions.Contains(requested) ? requested : LegacyVersions[0],
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = ServerInfo(),
                    ["instructions"] = Instructions,
                };
            case "server/discover":
                if (!modern) throw new RpcException(-32602, $"Invalid params: server/discover needs _meta[\"{VersionKey}\"]");
                return new JsonObject
                {
                    ["supportedVersions"] = new JsonArray([ModernVersion, .. LegacyVersions.Select(v => (JsonNode?)v)]),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["instructions"] = Instructions,
                };
            case "ping":
                return [];
            case "tools/list":
                return new JsonObject
                {
                    ["tools"] = new JsonArray([.. McpTools.List.Select(tool => (JsonNode?)new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["inputSchema"] = JsonNode.Parse(tool.InputSchema),
                        ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["idempotentHint"] = true, ["openWorldHint"] = false },
                    })]),
                };
            case "tools/call":
                var name = parameters?["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var toolName) ? toolName : null;
                if (name is null || McpTools.List.All(tool => tool.Name != name))
                {
                    throw new RpcException(-32602, name is null ? "Invalid params: tools/call needs a tool name" : $"Invalid params: unknown tool {name}; tools/list names them");
                }

                if (parameters?["arguments"] is { } arguments and not JsonObject)
                {
                    throw new RpcException(-32602, "Invalid params: arguments must be an object");
                }

                var result = await call(name, parameters?["arguments"]?.DeepClone().AsObject(), cancellationToken);
                return new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
                    ["structuredContent"] = result.Structured.DeepClone(),
                    ["isError"] = result.IsError,
                };
            default:
                throw new RpcException(-32601, $"Method not found: {method}");
        }
    }

    // A request whose _meta names a protocol version is modern; one without is served as the initialize-handshake era.
    private static bool Modern(JsonObject? parameters)
    {
        if (parameters?["_meta"] is not JsonObject meta || !meta.ContainsKey(VersionKey))
        {
            return false;
        }

        var requested = meta[VersionKey] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (requested != ModernVersion && (requested is null || !LegacyVersions.Contains(requested)))
        {
            throw new RpcException(-32022, "Unsupported protocol version", new JsonObject
            {
                ["supported"] = new JsonArray([ModernVersion, .. LegacyVersions.Select(v => (JsonNode?)v)]),
                ["requested"] = requested,
            });
        }

        if (requested == ModernVersion && meta[CapabilitiesKey] is not JsonObject)
        {
            throw new RpcException(-32602, $"Invalid params: _meta[\"{CapabilitiesKey}\"] is required");
        }

        return requested == ModernVersion;
    }

    private JsonObject ServerInfo() => new() { ["name"] = "codemuster", ["version"] = version };

    private static JsonNode? IdOf(JsonNode? message) => message is JsonObject request ? request["id"]?.DeepClone() : null;

    private static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null)
        {
            error["data"] = data;
        }

        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error };
    }
}
