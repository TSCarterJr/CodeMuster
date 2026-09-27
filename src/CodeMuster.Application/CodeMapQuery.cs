using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>How <c>map callers</c>, <c>map callees</c> and <c>map flow</c> print their graph (D62).</summary>
public enum MapFormat
{
    /// <summary>An indented tree, one edge per line with its kind, path:line and signature.</summary>
    Text,

    /// <summary>A Mermaid <c>flowchart TD</c>.</summary>
    Mermaid,

    /// <summary>A machine-readable graph of nodes and edges.</summary>
    Json,
}

/// <summary>One <c>map</c> invocation.</summary>
/// <param name="Subcommand">callers, callees or flow; null for the summary.</param>
/// <param name="Target">The symbol (callers, callees) or entry point (flow) to start from.</param>
/// <param name="Depth">How many edges to walk; null uses 1 for callers and callees and 6 for flow.</param>
/// <param name="Format">How to print the result.</param>
/// <param name="Page">True to return the data an interactive HTML page embeds instead of <paramref name="Format"/>.</param>
public sealed record MapRequest(string? Subcommand, string? Target, int? Depth, MapFormat Format, bool Page);

/// <summary>What <c>map</c> prints.</summary>
/// <param name="ExitCode">0 on success; 2 when there is no map yet or the target matches no symbol or several.</param>
/// <param name="Output">The rendered summary, graph, or page data; empty on failure.</param>
/// <param name="Error">Why the query failed, without an <c>error:</c> prefix; empty on success.</param>
public sealed record MapResult(int ExitCode, string Output, string Error);

/// <summary>
/// Answers questions about the code map the latest scan stored (D60, D62) and never scans.
/// Walks follow edges breadth-first from one symbol, stop at the requested depth and at <see cref="NodeCap"/> nodes, never revisit a node, and report how many reachable nodes they left out.
/// </summary>
public sealed class CodeMapQuery(ILedger ledger)
{
    /// <summary>The most nodes one walk returns.</summary>
    public const int NodeCap = 300;

    private const int CallDepth = 1;
    private const int FlowDepth = 6;
    private const int SummaryEntryPoints = 50;
    private const string Narrower = "use --depth or a narrower start";

    /// <summary>Reads the stored map and answers <paramref name="request"/>.</summary>
    public async Task<MapResult> RunAsync(MapRequest request, CancellationToken cancellationToken)
    {
        var stored = await ledger.GetCodeMapAsync(cancellationToken);
        if (stored is null)
        {
            return Fail("no code map yet; run codemuster scan");
        }

        var index = new MapIndex(stored);
        if (request.Subcommand is null)
        {
            return Ok(request.Page ? PageData(index, null) : request.Format == MapFormat.Json ? SummaryJson(index) : Summary(index));
        }

        var target = request.Target ?? "";
        MapView view;
        if (request.Subcommand == "flow")
        {
            var entries = index.FindEntryPoints(target);
            if (entries.Count != 1)
            {
                return Fail(entries.Count == 0
                    ? RewrittenByGitBash(target) + $"no entry point matches \"{target}\"; entry points:\n" + EntryList(index, index.EntryPoints)
                    : $"\"{target}\" matches {entries.Count} entry points; use one of these symbol ids:\n" + string.Join('\n', entries.Select(entry => $"  {entry.Display} ({entry.Kind})  {entry.SymbolId}")));
            }

            view = new MapView(entries[0].SymbolId, Callers: false, request.Depth ?? FlowDepth, entries[0]);
        }
        else
        {
            var symbols = index.FindSymbols(target);
            if (symbols.Count != 1)
            {
                return Fail(symbols.Count == 0
                    ? $"no symbol matches \"{target}\"\n  give a method or function name, Type.Method, part of a name, or an exact symbol id"
                    : $"\"{target}\" matches {symbols.Count} symbols; use a more exact name or one of these ids:\n"
                        + string.Join('\n', symbols.Select(symbol => $"  {MapIndex.ShortName(symbol)}  {symbol.Path}:{symbol.Range.StartLine}  {symbol.Id}")));
            }

            view = new MapView(symbols[0].Id, request.Subcommand == "callers", request.Depth ?? CallDepth, null);
        }

        if (request.Page)
        {
            return Ok(PageData(index, view));
        }

        var graph = index.Walk(view);
        return Ok(request.Format switch
        {
            MapFormat.Mermaid => Mermaid(graph),
            MapFormat.Json => Json(graph),
            _ => Text(graph),
        });
    }

    private static MapResult Ok(string output) => new(0, output, "");

    private static MapResult Fail(string error) => new(2, "", error);

    private static string Kind(EdgeKind kind) => kind.ToString().ToLowerInvariant();

    private static string Truncated(int count) => string.Create(CultureInfo.InvariantCulture, $"truncated: {count} more nodes; {Narrower}");

    private static string EntryList(MapIndex index, IEnumerable<EntryPoint> entries) =>
        string.Join('\n', entries.Select(entry => $"  {entry.Display} ({entry.Kind})  {index.Name(entry.SymbolId)}"));

    private static string Summary(MapIndex index)
    {
        var stored = index.Stored;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"code map at {Short(stored.HeadCommit)} (scanned {stored.ScannedAt})\n");
        text.Append(CultureInfo.InvariantCulture, $"symbols: {index.Symbols.Count}{Counts(index.Symbols.Select(symbol => Languages.FromPath(symbol.Path)))}\n");
        text.Append(CultureInfo.InvariantCulture, $"edges: {index.Edges.Count}{Counts(index.Edges.Select(edge => Kind(edge.Kind)))}\n");
        text.Append(CultureInfo.InvariantCulture, $"entry points: {index.EntryPoints.Count}{Counts(index.EntryPoints.Select(entry => entry.Kind))}\n");
        if (index.EntryPoints.Count > 0)
        {
            text.Append(EntryList(index, index.EntryPoints.Take(SummaryEntryPoints))).Append('\n');
            if (index.EntryPoints.Count > SummaryEntryPoints)
            {
                text.Append(CultureInfo.InvariantCulture, $"  ... and {index.EntryPoints.Count - SummaryEntryPoints} more; codemuster map --format json lists them all\n");
            }
        }

        var http = stored.Map.Diagnostics.Where(IsHttp).ToList();
        if (http.Count > 0)
        {
            text.Append("ui to api:\n");
            foreach (var diagnostic in http)
            {
                text.Append("  ").Append(diagnostic[HttpCall.DiagnosticPrefix.Length..]).Append('\n');
            }
        }

        if (stored.IsPartial)
        {
            text.Append(stored.FailedLanguages.Count > 0
                ? $"warning: partial map; failed languages: {string.Join(", ", stored.FailedLanguages)}\n"
                : "warning: partial map; the mappers reported:\n");
            foreach (var line in stored.Map.Diagnostics.Where(diagnostic => !IsHttp(diagnostic)).SelectMany(diagnostic => diagnostic.Split('\n')))
            {
                text.Append("  ").Append(line.TrimEnd('\r')).Append('\n');
            }
        }

        text.Append("next:\n");
        text.Append("  codemuster map flow \"<entry point>\" [--depth N]\n");
        text.Append("  codemuster map callers <symbol> [--depth N]\n");
        text.Append("  codemuster map callees <symbol> [--depth N]\n");
        text.Append("  add --format mermaid|json, or --out map.html for an interactive page\n");
        return text.ToString();
    }

    private static bool IsHttp(string diagnostic) => diagnostic.StartsWith(HttpCall.DiagnosticPrefix, StringComparison.Ordinal);

    // Git Bash turns an argument such as /quotes into its own install path (C:/Program Files/Git/quotes) before a program sees it.
    private static string RewrittenByGitBash(string target)
    {
        var match = Regex.Match(target, @"^[A-Za-z]:[\\/].*?[\\/]Git[\\/](?<route>.*)$");
        return match.Success
            ? $"Git Bash rewrote /{match.Groups["route"].Value} into \"{target}\"; write it as {match.Groups["route"].Value} (or set MSYS_NO_PATHCONV=1)\n"
            : "";
    }

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    private static string Counts(IEnumerable<string> values)
    {
        var counts = values.GroupBy(value => value, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} {group.Count()}")).ToList();
        return counts.Count == 0 ? "" : $" ({string.Join(", ", counts)})";
    }

    private static JsonObject CountObject(IEnumerable<string> values)
    {
        var counts = new JsonObject();
        foreach (var group in values.GroupBy(value => value, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            counts[group.Key] = group.Count();
        }

        return counts;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);

    private static string SummaryJson(MapIndex index)
    {
        var stored = index.Stored;
        var summary = new JsonObject
        {
            ["commit"] = stored.HeadCommit,
            ["scannedAt"] = stored.ScannedAt,
            ["partial"] = stored.IsPartial,
            ["mappedLanguages"] = Strings(stored.MappedLanguages),
            ["failedLanguages"] = Strings(stored.FailedLanguages),
            ["diagnostics"] = Strings(stored.Map.Diagnostics),
            ["symbols"] = index.Symbols.Count,
            ["symbolsByLanguage"] = CountObject(index.Symbols.Select(symbol => Languages.FromPath(symbol.Path))),
            ["edges"] = index.Edges.Count,
            ["edgesByKind"] = CountObject(index.Edges.Select(edge => Kind(edge.Kind))),
            ["entryPoints"] = new JsonArray([.. index.EntryPoints.Select(entry => (JsonNode?)new JsonObject
            {
                ["display"] = entry.Display,
                ["kind"] = entry.Kind,
                ["symbol"] = entry.SymbolId,
                ["name"] = index.Name(entry.SymbolId),
            })]),
        };
        return summary.ToJsonString(Indented) + "\n";
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string PageData(MapIndex index, MapView? view)
    {
        var stored = index.Stored;
        var data = new JsonObject
        {
            ["commit"] = stored.HeadCommit,
            ["scannedAt"] = stored.ScannedAt,
            ["partial"] = stored.IsPartial,
            ["failedLanguages"] = Strings(stored.FailedLanguages),
            ["diagnostics"] = Strings(stored.Map.Diagnostics),
            ["cap"] = NodeCap,
            ["symbols"] = new JsonArray([.. index.Symbols.Select(symbol => (JsonNode?)new JsonObject
            {
                ["id"] = symbol.Id,
                ["name"] = MapIndex.ShortName(symbol),
                ["path"] = symbol.Path,
                ["line"] = symbol.Range.StartLine,
                ["kind"] = symbol.Kind,
                ["container"] = SymbolContainer.Of(symbol),
                ["signature"] = symbol.Signature,
            })]),
            ["edges"] = new JsonArray([.. index.Edges.Select(edge => (JsonNode?)EdgeObject(edge))]),
            ["entryPoints"] = new JsonArray([.. index.EntryPoints.Select(entry => (JsonNode?)new JsonObject
            {
                ["id"] = entry.SymbolId,
                ["kind"] = entry.Kind,
                ["display"] = entry.Display,
            })]),
            ["view"] = view is null ? null : new JsonObject
            {
                ["direction"] = view.Callers ? "callers" : "callees",
                ["root"] = view.Root,
                ["depth"] = view.Depth,
                ["entry"] = view.Entry?.Display,
            },
        };
        return data.ToJsonString();
    }

    private static JsonObject EdgeObject(Edge edge) => new() { ["from"] = edge.From, ["to"] = edge.To, ["kind"] = Kind(edge.Kind) };

    private static string Json(MapGraph graph)
    {
        var json = new JsonObject
        {
            ["root"] = graph.View.Root,
            ["direction"] = graph.View.Callers ? "callers" : "callees",
            ["depth"] = graph.View.Depth,
            ["entry"] = graph.View.Entry?.Display,
            ["nodes"] = new JsonArray([.. graph.Nodes.Select(node => (JsonNode?)new JsonObject
            {
                ["id"] = node.Id,
                ["name"] = node.Name,
                ["path"] = node.Symbol?.Path,
                ["line"] = node.Symbol?.Range.StartLine,
                ["kind"] = node.Symbol?.Kind,
                ["container"] = node.Symbol is null ? null : SymbolContainer.Of(node.Symbol),
            })]),
            ["edges"] = new JsonArray([.. graph.Edges.Select(edge => (JsonNode?)EdgeObject(edge))]),
            ["truncated"] = graph.Truncated,
        };
        return json.ToJsonString(Indented) + "\n";
    }

    private static string Text(MapGraph graph)
    {
        var view = graph.View;
        var root = graph.Nodes[0];
        var text = new StringBuilder();
        text.Append(view.Entry is { } entry
            ? $"flow {entry.Display} ({entry.Kind}) from {root.Name} ({root.Id}), depth {view.Depth.ToString(CultureInfo.InvariantCulture)}\n"
            : $"{(view.Callers ? "callers" : "callees")} of {root.Name} ({root.Id}), depth {view.Depth.ToString(CultureInfo.InvariantCulture)}\n");
        text.Append(Describe(root)).Append('\n');
        var next = graph.Edges.ToLookup(edge => view.Callers ? edge.To : edge.From, StringComparer.Ordinal);
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var shown = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        if (!next[root.Id].Any())
        {
            text.Append(view.Callers ? "  (no callers)\n" : "  (no callees)\n");
        }

        Visit(root.Id, "  ");
        if (graph.Truncated > 0)
        {
            text.Append(Truncated(graph.Truncated)).Append('\n');
        }

        return text.ToString();

        void Visit(string id, string indent)
        {
            foreach (var edge in next[id])
            {
                var other = nodes[view.Callers ? edge.From : edge.To];
                text.Append(indent).Append(view.Callers ? "<- " : "-> ").Append(Kind(edge.Kind)).Append("  ");
                if (shown.Add(other.Id))
                {
                    text.Append(Describe(other)).Append('\n');
                    Visit(other.Id, indent + "  ");
                }
                else
                {
                    text.Append(other.Name).Append(other.Symbol is { } symbol ? $"  {symbol.Path}:{symbol.Range.StartLine.ToString(CultureInfo.InvariantCulture)}" : "").Append("  (see above)\n");
                }
            }
        }
    }

    private static string Describe(MapNode node) => node.Symbol is { } symbol
        ? $"{node.Name}  {symbol.Path}:{symbol.Range.StartLine.ToString(CultureInfo.InvariantCulture)}  {symbol.Signature.Split('\n')[^1].Trim()}"
        : $"{node.Name}  (not in the map)";

    private static string Mermaid(MapGraph graph)
    {
        var ids = graph.Nodes.Select((node, i) => (node.Id, Key: "n" + i.ToString(CultureInfo.InvariantCulture))).ToDictionary(pair => pair.Id, pair => pair.Key, StringComparer.Ordinal);
        var text = new StringBuilder("flowchart TD\n");
        if (graph.View.Entry is { } entry)
        {
            text.Append(CultureInfo.InvariantCulture, $"  entry([\"{Escape(entry.Display)}\"])\n");
        }

        foreach (var node in graph.Nodes)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {ids[node.Id]}[\"{Escape(node.Name)}\"]\n");
        }

        if (graph.View.Entry is not null)
        {
            text.Append("  entry ==> n0\n");
        }

        foreach (var edge in graph.Edges)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {ids[edge.From]} -->|{Kind(edge.Kind)}| {ids[edge.To]}\n");
        }

        text.Append("  classDef entry stroke-width:3px,font-weight:bold\n");
        text.Append(graph.View.Entry is null ? "  class n0 entry\n" : "  class entry entry\n");
        if (graph.Truncated > 0)
        {
            text.Append("  %% ").Append(Truncated(graph.Truncated)).Append('\n');
        }

        return text.ToString();
    }

    // Mermaid reads #name; and #number; as entities inside quoted labels, so '#' goes first and every other code starts with it.
    private static string Escape(string label) => label
        .Replace("#", "#35;", StringComparison.Ordinal)
        .Replace("\"", "#quot;", StringComparison.Ordinal)
        .Replace("<", "#lt;", StringComparison.Ordinal)
        .Replace(">", "#gt;", StringComparison.Ordinal)
        .ReplaceLineEndings(" ");

    private sealed record MapView(string Root, bool Callers, int Depth, EntryPoint? Entry);

    private sealed record MapNode(string Id, string Name, Symbol? Symbol);

    private sealed record MapGraph(MapView View, IReadOnlyList<MapNode> Nodes, IReadOnlyList<Edge> Edges, int Truncated);

    /// <summary>The stored map with duplicate symbols and edges removed and edges indexed in both directions.</summary>
    private sealed class MapIndex
    {
        private readonly Dictionary<string, Symbol> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Edge>> outgoing = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Edge>> incoming = new(StringComparer.Ordinal);

        public MapIndex(StoredCodeMap stored)
        {
            Stored = stored;
            var symbols = new List<Symbol>();
            foreach (var symbol in stored.Map.Symbols)
            {
                if (byId.TryAdd(symbol.Id, symbol))
                {
                    symbols.Add(symbol);
                }
            }

            Symbols = symbols;
            Edges = stored.Map.Edges.Distinct().ToList();
            foreach (var edge in Edges)
            {
                Add(outgoing, edge.From, edge);
                Add(incoming, edge.To, edge);
            }

            EntryPoints = stored.Map.EntryPoints.Distinct().ToList();
        }

        public StoredCodeMap Stored { get; }

        public IReadOnlyList<Symbol> Symbols { get; }

        public IReadOnlyList<Edge> Edges { get; }

        public IReadOnlyList<EntryPoint> EntryPoints { get; }

        private static void Add(Dictionary<string, List<Edge>> index, string key, Edge edge)
        {
            if (!index.TryGetValue(key, out var edges))
            {
                index[key] = edges = [];
            }

            edges.Add(edge);
        }

        public string Name(string id) => byId.TryGetValue(id, out var symbol) ? ShortName(symbol) : id;

        /// <summary>Type and member for C# (<c>QuoteService.ListQuotes</c>), the name after '#' for TypeScript.</summary>
        public static string ShortName(Symbol symbol)
        {
            var file = symbol.Path + "#";
            if (symbol.Id.StartsWith(file, StringComparison.Ordinal))
            {
                return symbol.Id[file.Length..];
            }

            var member = Qualified(symbol);
            var container = SymbolContainer.Of(symbol);
            if (container.Length == 0 || !member.StartsWith(container + ".", StringComparison.Ordinal))
            {
                return member;
            }

            var type = container[(container.LastIndexOf('.') + 1)..];
            return type + member[container.Length..];
        }

        // The id without its C# documentation prefix and parameter list, or the TypeScript id as it is.
        private static string Qualified(Symbol symbol)
        {
            var id = symbol.Id;
            if (id.StartsWith(symbol.Path + "#", StringComparison.Ordinal))
            {
                return id;
            }

            var member = id.Length > 1 && id[1] == ':' ? id[2..] : id;
            var end = member.IndexOfAny(['(', '~']);
            return end < 0 ? member : member[..end];
        }

        public IReadOnlyList<Symbol> FindSymbols(string query)
        {
            if (byId.TryGetValue(query, out var exact))
            {
                return [exact];
            }

            var named = Symbols.Where(symbol =>
            {
                var qualified = Qualified(symbol);
                return qualified.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || qualified.EndsWith("." + query, StringComparison.OrdinalIgnoreCase)
                    || qualified.EndsWith("#" + query, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (named.Count == 0)
            {
                named = Symbols.Where(symbol => Qualified(symbol).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return named.OrderBy(ShortName, StringComparer.Ordinal).ThenBy(symbol => symbol.Id, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<EntryPoint> FindEntryPoints(string query)
        {
            List<EntryPoint> matches = [.. EntryPoints.Where(entry => entry.Display.Equals(query, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0)
            {
                matches = [.. EntryPoints.Where(entry => entry.SymbolId == query)];
            }

            if (matches.Count == 0)
            {
                matches = [.. EntryPoints.Where(entry => entry.Display.Equals("/" + query.TrimStart('/'), StringComparison.OrdinalIgnoreCase))];
            }

            if (matches.Count == 0)
            {
                matches = [.. EntryPoints.Where(entry => entry.Display.Contains(query, StringComparison.OrdinalIgnoreCase))];
            }

            return matches;
        }

        public MapGraph Walk(MapView view)
        {
            var levels = new Dictionary<string, int>(StringComparer.Ordinal) { [view.Root] = 0 };
            var order = new List<string> { view.Root };
            var edges = new List<Edge>();
            var seen = new HashSet<Edge>();
            var queue = new Queue<string>([view.Root]);
            while (queue.TryDequeue(out var id))
            {
                if (levels[id] >= view.Depth)
                {
                    continue;
                }

                foreach (var edge in Next(id, view.Callers))
                {
                    var other = view.Callers ? edge.From : edge.To;
                    if (!levels.ContainsKey(other))
                    {
                        if (order.Count >= NodeCap)
                        {
                            continue;
                        }

                        levels[other] = levels[id] + 1;
                        order.Add(other);
                        queue.Enqueue(other);
                    }

                    if (seen.Add(edge))
                    {
                        edges.Add(edge);
                    }
                }
            }

            var nodes = order.Select(id => new MapNode(id, Name(id), byId.GetValueOrDefault(id))).ToList();
            return new MapGraph(view, nodes, edges, Reachable(view.Root, view.Callers) - nodes.Count);
        }

        private List<Edge> Next(string id, bool callers) => (callers ? incoming : outgoing).GetValueOrDefault(id) ?? [];

        private int Reachable(string root, bool callers)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal) { root };
            var queue = new Queue<string>([root]);
            while (queue.TryDequeue(out var id))
            {
                foreach (var edge in Next(id, callers))
                {
                    var other = callers ? edge.From : edge.To;
                    if (reached.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            return reached.Count;
        }
    }
}
