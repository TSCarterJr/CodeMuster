using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>One tool the <c>mcp</c> server offers (D73).</summary>
/// <param name="Name">The name an agent calls it by.</param>
/// <param name="Description">What it answers, written for the agent that chooses it.</param>
/// <param name="InputSchema">The JSON Schema of its arguments, as JSON text.</param>
public sealed record McpTool(string Name, string Description, string InputSchema);

/// <summary>What one tool call returns (D73).</summary>
/// <param name="Text">A concise answer for the agent to read.</param>
/// <param name="Structured">The same answer as JSON, with the map it came from and the cited files that changed since that scan.</param>
/// <param name="IsError">True when the call could not be answered, such as with no map or an ambiguous symbol.</param>
public sealed record McpToolResult(string Text, JsonObject Structured, bool IsError)
{
    /// <summary>A failed call whose structured answer holds only the error.</summary>
    public static McpToolResult Failure(string text) => new(text, new JsonObject { ["error"] = text }, true);
}

/// <summary>
/// The read-only tools <c>codemuster mcp</c> serves over the stored code map (D60, D72, D73). Never writes the ledger, calls a model or changes files.
/// The map is loaded once and reloaded when a later scan records a run; every answer names the cited files whose content changed since that scan.
/// With <c>refresh</c>, a call on a stale map first runs the given refresh (a scan) and shows what it said.
/// </summary>
public sealed class McpTools(ILedger ledger, ISourceTree tree, IContentHasher hasher, Func<CancellationToken, Task<string>>? refresh = null)
{
    /// <summary>The deepest callers or callees walk.</summary>
    public const int MaxDepth = 6;

    private const int PathDepth = 12;
    private const int ReferenceGroupCap = 50;

    private const string SymbolArgument = "A method, function, type, property, field or constant: its exact id, Type.Member, a bare name, or part of one. Ambiguous names return the candidates with their ids.";

    private static readonly Regex HttpDiagnostic = new(@"^http: (?<path>[^\s]+):(?<line>\d+) (?<text>.+)$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private State? state;
    private string? stamp;
    private bool loaded;

    /// <summary>Every tool, in the order <c>tools/list</c> returns them.</summary>
    public static IReadOnlyList<McpTool> List { get; } =
    [
        new("find_symbol",
            "Find methods, functions, classes, properties, fields and constants in this repository's code map by name, Type.Member, id or file path fragment. "
            + "Returns each match's id, kind, path:line, signature and container. Use it to get the exact id before calling references, callers or impact, instead of searching text.",
            Schema(("query", "string", "A name, Type.Member, exact id, or part of a file path.", true), ("kind", "string", "Only this kind, such as method, function, class, property.", false), ("limit", "integer", "Most matches to return (default 20, max 200).", false))),
        new("references",
            "Every place that uses a symbol or declaration: calls, reads, writes, type uses, inheritance, attributes and imports, each with path, line, column and the containing symbol; "
            + "for an HTTP endpoint also the UI calls that reach it (kind http). Use it for 'who uses this' before changing a method, DTO, setting or enum. Reflection and string lookups are not resolved.",
            Schema(("symbol", "string", SymbolArgument, true), ("kind", "string", "Only this kind: call, read, write, type, inherit, implement, attribute, import or http.", false), ("limit", "integer", "Most references to return (default 100, max 1000).", false))),
        new("callers",
            "Who calls a symbol, walking call edges (direct calls, DI-bound interface calls, implementations, overrides and UI-to-API http links) up to depth levels, with each call site's line and column when the map records them.",
            Schema(("symbol", "string", SymbolArgument, true), ("depth", "integer", "Levels to walk (default 1, max 6).", false))),
        new("callees",
            "What a symbol calls, walking call edges down to depth levels, including UI-to-API http links and DI-bound implementations, with call-site lines and columns when the map records them.",
            Schema(("symbol", "string", SymbolArgument, true), ("depth", "integer", "Levels to walk (default 1, max 6).", false))),
        new("call_path",
            "How execution reaches a symbol: the shortest call path from each entry point (HTTP endpoint, background service, UI page) that reaches it, or from a given symbol. Use it to learn how a method is invoked in production.",
            Schema(("to", "string", SymbolArgument, true), ("from", "string", "Start here instead of at the entry points.", false), ("limit", "integer", "Most paths to return (default 10, max 50).", false))),
        new("impact",
            "The blast radius of changing a symbol: every caller up to the entry points and UI pages it serves, the symbols it calls, and, where the map has references, its readers, writers and type users "
            + "and the entry points those reach. Call it before changing a signature, return value, DTO or constant.",
            Schema(("symbol", "string", SymbolArgument, true))),
        new("http_links",
            "The static links between UI HTTP calls (fetch, axios) and the C# endpoints that serve them: the UI functions that call an endpoint, or the endpoints a UI function calls, plus calls that match no endpoint. With no argument, every link.",
            Schema(("endpoint", "string", "An endpoint such as \"GET /quotes/{id}\", or part of one.", false), ("symbol", "string", "A UI function or an endpoint handler. " + SymbolArgument, false), ("limit", "integer", "Most links to return (default 100, max 1000).", false))),
        new("entry_points",
            "Where execution enters the codebase: HTTP endpoints (method and route), background services, and UI pages, each with its handler symbol and path:line.",
            Schema(("kind", "string", "Only http, background or page.", false), ("limit", "integer", "Most entry points to return (default 200, max 1000).", false))),
        new("duplicates",
            "Groups of methods and functions whose bodies are the same apart from names and literals (at least six lines), for finding code to reuse or consolidate. With a symbol, only its group.",
            Schema(("symbol", "string", SymbolArgument, false), ("limit", "integer", "Most groups to return (default 50, max 500).", false))),
    ];

    /// <summary>Answers one call of the tool named <paramref name="name"/>; an unknown name throws <see cref="ArgumentException"/>.</summary>
    public async Task<McpToolResult> CallAsync(string name, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (List.All(tool => tool.Name != name))
        {
            throw new ArgumentException($"unknown tool {name}", nameof(name));
        }

        var args = arguments ?? [];
        var notes = new List<string>();
        var current = await LoadAsync(cancellationToken);
        if (current is not null && refresh is not null && await IsStaleAsync(current, cancellationToken))
        {
            notes.Add(await refresh(cancellationToken));
            current = await LoadAsync(cancellationToken);
        }

        if (current is null)
        {
            return McpToolResult.Failure("no code map yet; run codemuster scan (after codemuster init), then call this tool again");
        }

        Answer answer;
        try
        {
            answer = name switch
            {
                "find_symbol" => FindSymbol(current, args),
                "references" => References(current, args),
                "callers" => Walk(current, args, callers: true, notes),
                "callees" => Walk(current, args, callers: false, notes),
                "call_path" => CallPath(current, args),
                "impact" => await ImpactAsync(current, args, cancellationToken),
                "http_links" => HttpLinks(current, args),
                "entry_points" => EntryPoints(current, args),
                _ => Duplicates(current, args),
            };
        }
        catch (ToolException ex)
        {
            answer = ex.Answer;
        }

        notes.AddRange(answer.Notes);
        var stale = await StaleAsync(current, answer.Paths, cancellationToken);
        var data = answer.Data;
        data["map"] = new JsonObject { ["commit"] = current.Stored.HeadCommit, ["scannedAt"] = current.Stored.ScannedAt, ["partial"] = current.Stored.IsPartial };
        data["notes"] = Strings(notes);
        data["stale"] = Strings(stale);
        var text = new StringBuilder(answer.Text.TrimEnd('\n')).Append('\n');
        foreach (var note in notes)
        {
            text.Append("note: ").Append(note).Append('\n');
        }

        if (stale.Count > 0)
        {
            text.Append("stale: ").Append(string.Join(", ", stale)).Append(" changed since the scan at ").Append(Short(current.Stored.HeadCommit))
                .Append("; lines may have moved, run codemuster scan to refresh the map\n");
        }

        return new McpToolResult(text.ToString(), data, answer.IsError);
    }

    /// <summary>
    /// <c>map references &lt;symbol&gt;</c>: what the <c>references</c> tool answers, as its text or its structured JSON, with up to the tool's most references;
    /// exit code 2 with the tool's message when there is no map or the symbol matches none or several.
    /// </summary>
    public async Task<MapResult> ReferencesAsync(string symbol, string? kind, MapFormat format, CancellationToken cancellationToken)
    {
        var arguments = new JsonObject { ["symbol"] = symbol, ["limit"] = 1000 };
        if (kind is not null)
        {
            arguments["kind"] = kind;
        }

        var result = await CallAsync("references", arguments, cancellationToken);
        return result.IsError
            ? new MapResult(2, "", result.Text.TrimEnd('\n'))
            : new MapResult(0, format == MapFormat.Json ? result.Structured.ToJsonString(Indented) + "\n" : result.Text, "");
    }

    private sealed record State(StoredCodeMap Stored, CodeMapQuery.MapIndex Index, IReadOnlyList<Symbol> All, IReadOnlyDictionary<string, Symbol> ById,
        ILookup<string, Reference> ReferencesTo, ILookup<(string From, string To), Reference> CallSites, IReadOnlySet<string> ReferenceLanguages, IReadOnlyDictionary<string, FileRecord> Files);

    private sealed record Answer(string Text, JsonObject Data, IEnumerable<string> Paths, bool IsError = false)
    {
        public IReadOnlyList<string> Notes { get; init; } = [];
    }

    private sealed class ToolException(Answer answer) : Exception(answer.Text)
    {
        public Answer Answer { get; } = answer;
    }

    private async Task<State?> LoadAsync(CancellationToken cancellationToken)
    {
        // Every scan records a run with the time it also stamps on the map, so the last run is the cheap way to see a new map.
        var run = (await ledger.GetLastRunAsync(cancellationToken))?.StartedAt;
        if (loaded && run == stamp)
        {
            return state;
        }

        var stored = await ledger.GetCodeMapAsync(cancellationToken);
        var files = (await ledger.GetFilesAsync(cancellationToken)).Where(file => file.DeletedAt is null).ToDictionary(file => file.Path, StringComparer.Ordinal);
        (loaded, stamp) = (true, run);
        if (stored is null)
        {
            return state = null;
        }

        var byId = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var all = new List<Symbol>();
        foreach (var symbol in stored.Map.Symbols.Concat(stored.Map.Declarations))
        {
            if (byId.TryAdd(symbol.Id, symbol))
            {
                all.Add(symbol);
            }
        }

        var references = stored.Map.References.Distinct().ToList();
        return state = new State(stored, new CodeMapQuery.MapIndex(stored), all, byId,
            references.ToLookup(reference => reference.To, StringComparer.Ordinal),
            references.Where(reference => reference.Kind == ReferenceKind.Call).ToLookup(reference => (reference.From, reference.To)),
            references.Select(reference => Languages.FromPath(reference.Path)).ToHashSet(StringComparer.Ordinal),
            files);
    }

    private async Task<IReadOnlyList<string>> StaleAsync(State current, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var cited = paths.Where(current.Files.ContainsKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (cited.Count == 0)
        {
            return [];
        }

        var listed = (await tree.ListFilesAsync(cancellationToken)).Where(file => cited.Contains(file.Path, StringComparer.Ordinal)).ToList();
        var hashes = await ContentHashes.CurrentAsync(hasher, listed, current.Files, cancellationToken);
        return cited.Where(path => !hashes.TryGetValue(path, out var hash) || hash != current.Files[path].ContentHash).ToList();
    }

    // Stale for refresh: a scanned file changed or vanished, or a new file of a mapped language appeared.
    private async Task<bool> IsStaleAsync(State current, CancellationToken cancellationToken)
    {
        var included = current.Files.Values.Where(file => file.ExcludedReason is null).ToDictionary(file => file.Path, StringComparer.Ordinal);
        var listed = await tree.ListFilesAsync(cancellationToken);
        if (listed.Any(file => !current.Files.ContainsKey(file.Path) && current.Stored.MappedLanguages.Contains(Languages.FromPath(file.Path))))
        {
            return true;
        }

        var present = listed.Where(file => included.ContainsKey(file.Path)).ToList();
        if (present.Count < included.Count)
        {
            return true;
        }

        var hashes = await ContentHashes.CurrentAsync(hasher, present, current.Files, cancellationToken);
        return present.Any(file => hashes[file.Path] != included[file.Path].ContentHash);
    }

    private static Answer FindSymbol(State current, JsonObject args)
    {
        var query = Required(args, "query");
        var kind = Text(args, "kind");
        var limit = Limit(args, "limit", 20, 200);
        var matches = CodeMapQuery.MapIndex.Match(current.All, query);
        if (matches.Count == 0)
        {
            matches = current.All.Where(symbol => symbol.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(symbol => symbol.Path, StringComparer.Ordinal).ThenBy(symbol => symbol.Range.StartLine).ToList();
        }

        var filtered = matches.Where(symbol => kind is null || symbol.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
        var shown = filtered.Take(limit).ToList();
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{filtered.Count} match{(filtered.Count == 1 ? "" : "es")} for \"{query}\"{(kind is null ? "" : $" of kind {kind}")}\n"));
        foreach (var symbol in shown)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {Name(symbol)}  {symbol.Kind}  {Location(symbol)}  {LastLine(symbol.Signature)}  {symbol.Id}\n");
        }

        AppendCut(text, filtered.Count - shown.Count, "raise limit or narrow the query");
        return new Answer(text.ToString(), new JsonObject
        {
            ["query"] = query,
            ["total"] = filtered.Count,
            ["results"] = new JsonArray([.. shown.Select(symbol => (JsonNode?)SymbolJson(symbol))]),
            ["truncated"] = filtered.Count - shown.Count,
        }, shown.Select(symbol => symbol.Path));
    }

    private static Answer References(State current, JsonObject args)
    {
        var target = Resolve(current, args, "symbol");
        var kind = Text(args, "kind")?.ToLowerInvariant();
        var limit = Limit(args, "limit", 100, 1000);
        var rows = current.ReferencesTo[target.Id]
            .Select(reference => (Kind: Lower(reference.Kind), reference.Path, reference.Line, Column: (int?)reference.Column, reference.From))
            .Concat(current.Index.Next(target.Id, callers: true).Where(edge => edge.Kind == EdgeKind.Http).Distinct()
                .Select(edge => current.ById.GetValueOrDefault(edge.From) is { } caller
                    ? (Kind: "http", caller.Path, Line: caller.Range.StartLine, Column: (int?)null, edge.From)
                    : (Kind: "http", Path: "", Line: 0, Column: (int?)null, edge.From)))
            .Where(row => kind is null || row.Kind == kind)
            .OrderBy(row => row.Path, StringComparer.Ordinal).ThenBy(row => row.Line).ThenBy(row => row.Column ?? 0)
            .ToList();
        var shown = rows.Take(limit).ToList();
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{rows.Count} reference{(rows.Count == 1 ? "" : "s")} to {Name(target)} ({target.Id}){(kind is null ? "" : $" of kind {kind}")}\n"));
        foreach (var row in shown)
        {
            var position = row.Column is { } column ? string.Create(CultureInfo.InvariantCulture, $"{row.Path}:{row.Line}:{column}") : string.Create(CultureInfo.InvariantCulture, $"{row.Path}:{row.Line}");
            text.Append(CultureInfo.InvariantCulture, $"  {row.Kind}  {position}  in {NameOf(current, row.From)}\n");
        }

        AppendCut(text, rows.Count - shown.Count, "raise limit or pass kind");
        return new Answer(text.ToString(), new JsonObject
        {
            ["symbol"] = SymbolJson(target),
            ["total"] = rows.Count,
            ["references"] = new JsonArray([.. shown.Select(row => (JsonNode?)new JsonObject
            {
                ["kind"] = row.Kind,
                ["path"] = row.Path,
                ["line"] = row.Line,
                ["column"] = row.Column,
                ["from"] = row.From,
                ["fromName"] = NameOf(current, row.From),
            })]),
            ["truncated"] = rows.Count - shown.Count,
        }, [target.Path, .. shown.Select(row => row.Path)])
        {
            Notes = ReferenceNotes(current, target),
        };
    }

    private static IReadOnlyList<string> ReferenceNotes(State current, Symbol target)
    {
        if (current.Stored.Map.References.Count == 0)
        {
            return ["references are unavailable: this map records none, because it was stored before ledger schema 9 or by mappers that do not record references yet; "
                + "run codemuster scan with a current codemuster. callers and callees still follow call edges"];
        }

        var language = Languages.FromPath(target.Path);
        return current.ReferenceLanguages.Contains(language)
            ? []
            : [$"no {language} references were recorded in this map, so uses in {language} files are missing (the {language} mapper did not record them); callers still follows call edges"];
    }

    private static Answer Walk(State current, JsonObject args, bool callers, List<string> notes)
    {
        var target = Resolve(current, args, "symbol");
        var depth = Limit(args, "depth", 1, int.MaxValue);
        if (depth > MaxDepth)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"depth {depth} is above the maximum of {MaxDepth}; walked {MaxDepth}"));
            depth = MaxDepth;
        }

        var graph = current.Index.Walk(new CodeMapQuery.MapView(target.Id, callers, depth, null));
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var direction = callers ? "callers" : "callees";
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{direction} of {Name(target)} ({target.Id}), depth {depth}\n"));
        var edges = new JsonArray();
        var paths = new List<string> { target.Path };
        foreach (var edge in graph.Edges)
        {
            var far = nodes[callers ? edge.From : edge.To];
            var sites = current.CallSites[(edge.From, edge.To)].OrderBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.Line).ThenBy(site => site.Column).ToList();
            var location = far.Symbol is { } symbol ? "  " + Location(symbol) : "  (not in the map)";
            var at = sites.Count == 0 ? "" : "  at " + string.Join(", ", sites.Select(site => string.Create(CultureInfo.InvariantCulture, $"{site.Path}:{site.Line}:{site.Column}")));
            text.Append(new string(' ', 2 * far.Depth)).Append(callers ? "<- " : "-> ").Append(Lower(edge.Kind)).Append("  ").Append(far.Name).Append(location).Append(at).Append('\n');
            if (far.Symbol is not null)
            {
                paths.Add(far.Symbol.Path);
            }

            paths.AddRange(sites.Select(site => site.Path));
            edges.Add(new JsonObject
            {
                ["from"] = edge.From,
                ["fromName"] = nodes[edge.From].Name,
                ["to"] = edge.To,
                ["toName"] = nodes[edge.To].Name,
                ["kind"] = Lower(edge.Kind),
                ["depth"] = far.Depth,
                ["path"] = far.Symbol?.Path,
                ["line"] = far.Symbol?.Range.StartLine,
                ["callSites"] = new JsonArray([.. sites.Select(site => (JsonNode?)new JsonObject { ["path"] = site.Path, ["line"] = site.Line, ["column"] = site.Column })]),
            });
        }

        if (graph.Edges.Count == 0)
        {
            text.Append(callers ? "  (no callers in the map)\n" : "  (no callees in the map)\n");
        }

        if (graph.Truncated > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"truncated: {graph.Truncated} more reachable symbols past the {CodeMapQuery.NodeCap}-node cap; use a smaller depth\n");
        }

        return new Answer(text.ToString(), new JsonObject
        {
            ["symbol"] = SymbolJson(target),
            ["direction"] = direction,
            ["depth"] = depth,
            ["edges"] = edges,
            ["truncated"] = graph.Truncated,
        }, paths)
        {
            Notes = current.Stored.Map.References.Count == 0 ? ["call-site lines and columns are unavailable: this map has no references, because it was stored before ledger schema 9 or by mappers that do not record references yet; the call edges are still shown"] : [],
        };
    }

    private static Answer CallPath(State current, JsonObject args)
    {
        var to = Resolve(current, args, "to");
        var from = Text(args, "from") is null ? null : Resolve(current, args, "from");
        var limit = Limit(args, "limit", 10, 50);
        var entries = current.Index.EntryPoints.ToLookup(entry => entry.SymbolId, StringComparer.Ordinal);
        bool IsStart(string id) => from is null ? entries[id].Any() : id == from.Id;

        // Breadth-first up the callers from the target: the first edge that reaches a symbol is its step on a shortest path.
        var next = new Dictionary<string, Edge?>(StringComparer.Ordinal) { [to.Id] = null };
        var starts = IsStart(to.Id) ? new List<string> { to.Id } : [];
        var queue = new Queue<(string Id, int Depth)>([(to.Id, 0)]);
        while (queue.TryDequeue(out var item))
        {
            if (item.Depth >= PathDepth)
            {
                continue;
            }

            foreach (var edge in current.Index.Next(item.Id, callers: true))
            {
                if (next.TryAdd(edge.From, edge))
                {
                    if (IsStart(edge.From))
                    {
                        starts.Add(edge.From);
                    }

                    queue.Enqueue((edge.From, item.Depth + 1));
                }
            }
        }

        var shown = starts.Take(limit).ToList();
        var text = new StringBuilder(from is null
            ? string.Create(CultureInfo.InvariantCulture, $"{starts.Count} entry point{(starts.Count == 1 ? "" : "s")} reach {Name(to)} ({to.Id}); shortest path from each\n")
            : $"path from {Name(from)} to {Name(to)}\n");
        var paths = new JsonArray();
        var cited = new List<string> { to.Path };
        foreach (var start in shown)
        {
            var steps = new List<(string Id, EdgeKind? Kind)>();
            string? id = start;
            while (id is not null)
            {
                var edge = next[id];
                steps.Add((id, edge?.Kind));
                id = edge?.To;
            }

            var label = from is null ? string.Join(", ", entries[start].Select(entry => entry.Display)) : Name(from);
            text.Append("  ").Append(label).Append(": ")
                .Append(string.Concat(steps.Select(step => NameOf(current, step.Id) + (step.Kind is { } kind ? $" -{Lower(kind)}-> " : "")))).Append('\n');
            cited.AddRange(steps.Select(step => current.ById.GetValueOrDefault(step.Id)?.Path).OfType<string>());
            paths.Add(new JsonObject
            {
                ["entry"] = label,
                ["steps"] = new JsonArray([.. steps.Select(step => (JsonNode?)new JsonObject
                {
                    ["id"] = step.Id,
                    ["name"] = NameOf(current, step.Id),
                    ["path"] = current.ById.GetValueOrDefault(step.Id)?.Path,
                    ["line"] = current.ById.GetValueOrDefault(step.Id)?.Range.StartLine,
                    ["edge"] = step.Kind is { } kind ? Lower(kind) : null,
                })]),
            });
        }

        if (starts.Count == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"  none within {PathDepth} calls\n");
        }

        AppendCut(text, starts.Count - shown.Count, "raise limit");
        return new Answer(text.ToString(), new JsonObject
        {
            ["to"] = SymbolJson(to),
            ["from"] = from is null ? null : SymbolJson(from),
            ["paths"] = paths,
            ["truncated"] = starts.Count - shown.Count,
            ["maxLength"] = PathDepth,
        }, cited);
    }

    private async Task<Answer> ImpactAsync(State current, JsonObject args, CancellationToken cancellationToken)
    {
        var target = Resolve(current, args, "symbol");
        var map = current.Stored.Map;
        var reach = ImpactReview.Walk(map, target.Id);
        var references = current.ReferencesTo[target.Id].ToList();
        var unit = (await ledger.GetUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Id == UnitIds.Impact(target.Id) && u.Status != UnitStatus.Retired);
        var text = new StringBuilder();
        ImpactQuery.Append(text, new ImpactQuery.Row(target, unit, reach), unit is null ? null : "impact unit " + unit.Status.ToString().ToLowerInvariant());
        text.Append("  callees: ").Append(reach.Callees.Count == 0 ? "none" : string.Join(", ", reach.Callees.Select(Name))).Append('\n');
        var groups = references.GroupBy(reference => reference.Kind).OrderBy(group => group.Key).ToList();
        foreach (var group in groups)
        {
            text.Append("  ").Append(GroupLabel(group.Key)).Append(": ")
                .Append(string.Join(", ", group.Take(ReferenceGroupCap).Select(reference => string.Create(CultureInfo.InvariantCulture, $"{NameOf(current, reference.From)} ({reference.Path}:{reference.Line})"))))
                .Append(group.Count() > ReferenceGroupCap ? string.Create(CultureInfo.InvariantCulture, $" and {group.Count() - ReferenceGroupCap} more") : "").Append('\n');
        }

        if (reach.Upward.Count >= CodeMapQuery.NodeCap)
        {
            text.Append(CultureInfo.InvariantCulture, $"truncated: callers stop at {CodeMapQuery.NodeCap} symbols\n");
        }

        var referenceData = new JsonObject();
        foreach (var group in groups)
        {
            referenceData[Lower(group.Key)] = new JsonArray([.. group.Take(ReferenceGroupCap).Select(reference => (JsonNode?)new JsonObject
            {
                ["from"] = reference.From,
                ["fromName"] = NameOf(current, reference.From),
                ["path"] = reference.Path,
                ["line"] = reference.Line,
                ["column"] = reference.Column,
            })]);
        }

        return new Answer(text.ToString(), new JsonObject
        {
            ["symbol"] = SymbolJson(target),
            ["impactUnit"] = unit?.Status.ToString().ToLowerInvariant(),
            ["callers"] = new JsonArray([.. reach.Upward.Select(caller => (JsonNode?)new JsonObject
            {
                ["id"] = caller.Symbol.Id,
                ["name"] = Name(caller.Symbol),
                ["path"] = caller.Symbol.Path,
                ["line"] = caller.Symbol.Range.StartLine,
                ["depth"] = caller.Depth,
            })]),
            ["entryPoints"] = new JsonArray([.. reach.EntryPoints.Select(pair => (JsonNode?)new JsonObject
            {
                ["display"] = pair.Entry.Display,
                ["kind"] = pair.Entry.Kind,
                ["symbol"] = pair.Entry.SymbolId,
                ["depth"] = pair.Depth,
            })]),
            ["callees"] = new JsonArray([.. reach.Callees.Select(callee => (JsonNode?)SymbolJson(callee))]),
            ["calleesCut"] = reach.CalleesCut,
            ["references"] = referenceData,
        }, [target.Path, .. reach.Upward.Select(caller => caller.Symbol.Path), .. reach.Callees.Select(callee => callee.Path), .. references.Take(ReferenceGroupCap).Select(reference => reference.Path)])
        {
            Notes = current.Stored.Map.References.Count == 0 ? ReferenceNotes(current, target) : [],
        };
    }

    private static string GroupLabel(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Read => "readers",
        ReferenceKind.Write => "writers",
        ReferenceKind.Call => "call sites",
        ReferenceKind.Type => "type users",
        ReferenceKind.Inherit => "subclasses",
        ReferenceKind.Implement => "implementations",
        ReferenceKind.Attribute => "attribute uses",
        _ => "imports",
    };

    private static Answer HttpLinks(State current, JsonObject args)
    {
        var endpoint = Text(args, "endpoint");
        var limit = Limit(args, "limit", 100, 1000);
        if (endpoint is not null && Text(args, "symbol") is not null)
        {
            throw Error("give endpoint or symbol, not both");
        }

        var http = current.Index.EntryPoints.Where(entry => entry.Kind == "http").ToList();
        var handled = http.ToLookup(entry => entry.SymbolId, StringComparer.Ordinal);
        var links = current.Index.Edges.Where(edge => edge.Kind == EdgeKind.Http).ToList();
        var diagnostics = current.Stored.Map.Diagnostics.Select(diagnostic => HttpDiagnostic.Match(diagnostic)).Where(match => match.Success)
            .Select(match => (Path: match.Groups["path"].Value, Line: int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture), Text: match.Groups["text"].Value)).ToList();
        var notes = new List<string>();
        string heading;
        if (endpoint is not null)
        {
            var matches = current.Index.FindEntryPoints(endpoint).Where(entry => entry.Kind == "http").ToList();
            if (matches.Count == 0)
            {
                throw Error($"no HTTP endpoint matches \"{endpoint}\"; endpoints: " + string.Join(", ", http.Take(30).Select(entry => entry.Display)) + (http.Count > 30 ? ", ..." : ""));
            }

            var handlers = matches.Select(entry => entry.SymbolId).ToHashSet(StringComparer.Ordinal);
            links = links.Where(edge => handlers.Contains(edge.To)).ToList();
            diagnostics = [];
            notes.AddRange(matches.Where(entry => current.Stored.Map.Diagnostics.Contains($"{HttpCall.DiagnosticPrefix}{entry.Display} is not called from the mapped UI")).Select(entry => $"{entry.Display} is not called from the mapped UI"));
            heading = $"UI calls to {string.Join(", ", matches.Select(entry => entry.Display))}";
        }
        else if (Text(args, "symbol") is not null)
        {
            var symbol = Resolve(current, args, "symbol");
            links = links.Where(edge => edge.From == symbol.Id || edge.To == symbol.Id).ToList();
            diagnostics = diagnostics.Where(d => d.Path == symbol.Path && d.Line >= symbol.Range.StartLine && d.Line <= symbol.Range.EndLine).ToList();
            heading = $"HTTP links of {Name(symbol)} ({symbol.Id})";
        }
        else
        {
            heading = "every UI-to-API link";
        }

        var shown = links.Take(limit).ToList();
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{heading}: {links.Count} link{(links.Count == 1 ? "" : "s")}\n"));
        foreach (var link in shown)
        {
            var caller = current.ById.GetValueOrDefault(link.From);
            var handler = current.ById.GetValueOrDefault(link.To);
            text.Append(CultureInfo.InvariantCulture, $"  {NameOf(current, link.From)}{(caller is null ? "" : "  " + Location(caller))}  -> {Display(handled, link.To)}  {NameOf(current, link.To)}{(handler is null ? "" : "  " + Location(handler))}\n");
        }

        AppendCut(text, links.Count - shown.Count, "raise limit or pass endpoint or symbol");
        var unmatched = diagnostics.Take(limit).ToList();
        if (unmatched.Count > 0)
        {
            text.Append("unmatched UI calls:\n");
            foreach (var (path, line, message) in unmatched)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {path}:{line} {message}\n");
            }
        }

        return new Answer(text.ToString(), new JsonObject
        {
            ["links"] = new JsonArray([.. shown.Select(link => (JsonNode?)new JsonObject
            {
                ["endpoint"] = Display(handled, link.To),
                ["handler"] = link.To,
                ["handlerName"] = NameOf(current, link.To),
                ["handlerPath"] = current.ById.GetValueOrDefault(link.To)?.Path,
                ["handlerLine"] = current.ById.GetValueOrDefault(link.To)?.Range.StartLine,
                ["from"] = link.From,
                ["fromName"] = NameOf(current, link.From),
                ["path"] = current.ById.GetValueOrDefault(link.From)?.Path,
                ["line"] = current.ById.GetValueOrDefault(link.From)?.Range.StartLine,
            })]),
            ["unmatched"] = new JsonArray([.. unmatched.Select(d => (JsonNode?)new JsonObject { ["path"] = d.Path, ["line"] = d.Line, ["text"] = d.Text })]),
            ["truncated"] = links.Count - shown.Count,
        }, [.. shown.SelectMany(link => new[] { link.From, link.To }).Select(id => current.ById.GetValueOrDefault(id)?.Path).OfType<string>(), .. unmatched.Select(d => d.Path)])
        {
            Notes = notes,
        };
    }

    private static string Display(ILookup<string, EntryPoint> handled, string symbolId) =>
        handled[symbolId].Any() ? string.Join(", ", handled[symbolId].Select(entry => entry.Display)) : symbolId;

    private static Answer EntryPoints(State current, JsonObject args)
    {
        var kind = Text(args, "kind");
        var limit = Limit(args, "limit", 200, 1000);
        var entries = current.Index.EntryPoints.Where(entry => kind is null || entry.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
        var shown = entries.Take(limit).ToList();
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{entries.Count} entry point{(entries.Count == 1 ? "" : "s")}{(kind is null ? "" : $" of kind {kind}")}\n"));
        foreach (var entry in shown)
        {
            var symbol = current.ById.GetValueOrDefault(entry.SymbolId);
            text.Append(CultureInfo.InvariantCulture, $"  {entry.Display} ({entry.Kind})  {NameOf(current, entry.SymbolId)}{(symbol is null ? "" : "  " + Location(symbol))}\n");
        }

        AppendCut(text, entries.Count - shown.Count, "raise limit or pass kind");
        return new Answer(text.ToString(), new JsonObject
        {
            ["entryPoints"] = new JsonArray([.. shown.Select(entry => (JsonNode?)new JsonObject
            {
                ["display"] = entry.Display,
                ["kind"] = entry.Kind,
                ["symbol"] = entry.SymbolId,
                ["name"] = NameOf(current, entry.SymbolId),
                ["path"] = current.ById.GetValueOrDefault(entry.SymbolId)?.Path,
                ["line"] = current.ById.GetValueOrDefault(entry.SymbolId)?.Range.StartLine,
            })]),
            ["truncated"] = entries.Count - shown.Count,
        }, shown.Select(entry => current.ById.GetValueOrDefault(entry.SymbolId)?.Path).OfType<string>());
    }

    private static Answer Duplicates(State current, JsonObject args)
    {
        var limit = Limit(args, "limit", 50, 500);
        var target = Text(args, "symbol") is null ? null : Resolve(current, args, "symbol");
        var map = current.Stored.Map;
        var groups = DuplicateReview.Groups(map, map.Symbols.Select(symbol => symbol.Path).ToHashSet(StringComparer.Ordinal))
            .Where(group => target is null || group.Copies.Any(copy => copy.Id == target.Id)).ToList();
        var shown = groups.Take(limit).ToList();
        var text = new StringBuilder();
        if (groups.Count == 0)
        {
            text.Append(target is null
                ? string.Create(CultureInfo.InvariantCulture, $"no duplicate groups: no two symbols of at least {DuplicateReview.MinLines} lines share a normalized body\n")
                : string.Create(CultureInfo.InvariantCulture, $"no copies of {Name(target)} with the same normalized body (at least {DuplicateReview.MinLines} lines)\n"));
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"{groups.Count} duplicate group{(groups.Count == 1 ? "" : "s")} (same body apart from names and literals)\n");
            foreach (var group in shown)
            {
                text.Append("  ").Append(string.Join(", ", group.Copies.Select(copy => $"{Name(copy)} ({Location(copy)})"))).Append('\n');
            }

            AppendCut(text, groups.Count - shown.Count, "raise limit or pass symbol");
        }

        return new Answer(text.ToString(), new JsonObject
        {
            ["groups"] = new JsonArray([.. shown.Select(group => (JsonNode?)new JsonObject
            {
                ["id"] = group.Id,
                ["copies"] = new JsonArray([.. group.Copies.Select(copy => (JsonNode?)SymbolJson(copy))]),
            })]),
            ["truncated"] = groups.Count - shown.Count,
        }, shown.SelectMany(group => group.Copies.Select(copy => copy.Path)).Append(target?.Path).OfType<string>())
        {
            Notes = map.Symbols.Any(symbol => symbol.NormalizedHash is not null) ? [] : ["this map has no normalized body hashes; run codemuster scan with a current codemuster"],
        };
    }

    private static Symbol Resolve(State current, JsonObject args, string argument)
    {
        var query = Required(args, argument);
        var matches = CodeMapQuery.MapIndex.Match(current.All, query);
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count == 0)
        {
            throw Error($"no symbol or declaration matches \"{query}\"; call find_symbol with part of the name or a file path");
        }

        var shown = matches.Take(20).ToList();
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"\"{query}\" matches {matches.Count} symbols; call again with one of these ids:\n"));
        foreach (var symbol in shown)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {Name(symbol)}  {Location(symbol)}  {symbol.Id}\n");
        }

        AppendCut(text, matches.Count - shown.Count, "use a more exact name");
        throw new ToolException(new Answer(text.ToString(), new JsonObject
        {
            ["error"] = $"\"{query}\" matches {matches.Count} symbols",
            ["candidates"] = new JsonArray([.. shown.Select(symbol => (JsonNode?)SymbolJson(symbol))]),
        }, shown.Select(symbol => symbol.Path), IsError: true));
    }

    private static ToolException Error(string message) => new(new Answer(message, new JsonObject { ["error"] = message }, [], IsError: true));

    private static string Required(JsonObject args, string name) =>
        Text(args, name) ?? throw Error($"{name} is required");

    private static string? Text(JsonObject args, string name) =>
        args[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static int Limit(JsonObject args, string name, int fallback, int max)
    {
        if (args[name] is null)
        {
            return fallback;
        }

        if (args[name] is JsonValue value && value.TryGetValue<int>(out var number) && number > 0)
        {
            return Math.Min(number, max);
        }

        throw Error($"{name} must be a positive whole number");
    }

    private static void AppendCut(StringBuilder text, int cut, string advice)
    {
        if (cut > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{cut} more not shown; {advice}\n");
        }
    }

    private static JsonObject SymbolJson(Symbol symbol) => new()
    {
        ["id"] = symbol.Id,
        ["name"] = Name(symbol),
        ["kind"] = symbol.Kind,
        ["path"] = symbol.Path,
        ["line"] = symbol.Range.StartLine,
        ["endLine"] = symbol.Range.EndLine,
        ["signature"] = LastLine(symbol.Signature),
        ["container"] = SymbolContainer.Of(symbol),
    };

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);

    private static string Name(Symbol symbol) => CodeMapQuery.ShortName(symbol);

    private static string NameOf(State current, string id) => current.ById.TryGetValue(id, out var symbol) ? Name(symbol) : id;

    private static string Location(Symbol symbol) => string.Create(CultureInfo.InvariantCulture, $"{symbol.Path}:{symbol.Range.StartLine}");

    private static string LastLine(string signature) => signature.Split('\n')[^1].Trim();

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    private static string Schema(params (string Name, string Type, string Description, bool Required)[] properties)
    {
        var shape = new JsonObject();
        foreach (var (name, type, description, _) in properties)
        {
            shape[name] = type == "integer"
                ? new JsonObject { ["type"] = type, ["description"] = description, ["minimum"] = 1 }
                : new JsonObject { ["type"] = type, ["description"] = description };
        }

        var schema = new JsonObject { ["type"] = "object", ["properties"] = shape, ["additionalProperties"] = false };
        var required = properties.Where(property => property.Required).Select(property => property.Name).ToList();
        if (required.Count > 0)
        {
            schema["required"] = Strings(required);
        }

        return schema.ToJsonString();
    }
}
