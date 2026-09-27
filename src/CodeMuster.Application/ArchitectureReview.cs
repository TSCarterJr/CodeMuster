using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// App-wide UX and API design review (D69): one <see cref="UnitKind.Architecture"/> unit over the UI's structure and one <see cref="UnitKind.Api"/> unit over the HTTP endpoints.
/// Their members are the files that define the elements or endpoints; each file member's hash also covers the structure read from it, so a unit reruns only when its input changes.
/// </summary>
public static class ArchitectureReview
{
    /// <summary>The lens id architecture findings carry.</summary>
    public const string UiLensId = "architecture";

    /// <summary>The lens id api findings carry.</summary>
    public const string ApiLensId = "api";

    /// <summary>What the architecture pack asks.</summary>
    public const string UiInstructions =
        "Below is the structure of the whole UI: every page route, navigation entry, section heading, and form control or setting with its label, "
        + "grouped by page and nested by section, each with the file and line that defines it. It is not code; review it as a user would find their way through it. "
        + "Report misplaced or duplicated settings and actions (such as a payment setting under General while a Payments section exists), inconsistent names for the same thing, "
        + "orphan pages that no navigation reaches, features buried too deep to find, and destructive actions placed next to safe ones. "
        + "Cite the file and line of the control, heading or entry, use category \"architecture\", and set lens_id to \"architecture\". Report nothing when the structure is coherent.";

    /// <summary>What the api pack asks.</summary>
    public const string ApiInstructions =
        "Below is every HTTP endpoint: its method and route, its handler, and the handler's signature with its attributes (such as [Authorize] or [AllowAnonymous] on the class or the action), "
        + "grouped by route prefix, each with the file and line that defines it. Review the API as a whole: inconsistent naming or pluralization of routes, verbs that do not match what the handler does, "
        + "inconsistent error shapes or pagination between similar endpoints, missing authorization in a group whose other endpoints require it, and duplicate endpoints. "
        + "Cite the file and line of the endpoint, use category \"api\", and set lens_id to \"api\". Report nothing when the API is consistent.";

    /// <summary>The architecture unit when an included file defines at least one UI element; members are those files.</summary>
    public static IReadOnlyList<PlannedUnit> PlanUi(CodeMap map, IReadOnlyList<FileRecord> included)
    {
        var files = included.ToDictionary(f => f.Path, f => f.ContentHash, StringComparer.Ordinal);
        var elements = map.UiElements.Where(e => files.ContainsKey(e.Path)).ToList();
        return elements.Count == 0 ? [] : [Unit(UnitIds.ArchitectureUi, UnitKind.Architecture, "UI structure", elements.ToLookup(e => e.Path, UiLine, StringComparer.Ordinal), files)];
    }

    /// <summary>The api unit when an included file defines at least one http entry point; members are those files.</summary>
    public static IReadOnlyList<PlannedUnit> PlanApi(CodeMap map, IReadOnlyList<FileRecord> included)
    {
        var files = included.ToDictionary(f => f.Path, f => f.ContentHash, StringComparer.Ordinal);
        var endpoints = Endpoints(map, files.Keys.ToHashSet(StringComparer.Ordinal));
        return endpoints.Count == 0 ? [] : [Unit(UnitIds.ArchitectureApi, UnitKind.Api, "API endpoints", endpoints.ToLookup(e => e.Symbol.Path, EndpointLine, StringComparer.Ordinal), files)];
    }

    /// <summary>The UI as a tree: one group per page route, then one per file without a route, each element nested under the heading it sits in.</summary>
    public static IReadOnlyList<string> RenderUi(IReadOnlyList<UiElement> elements)
    {
        var groups = elements
            .GroupBy(e => e.Route is { } route ? (Order: 0, Name: route, Title: route) : (Order: 1, Name: e.Path, Title: $"{e.Path} (no route)"))
            .OrderBy(g => g.Key.Order)
            .ThenBy(g => g.Key.Name, StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var group in groups)
        {
            if (lines.Count > 0) lines.Add("");
            lines.AddRange([$"### {group.Key.Title}", ""]);
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var element in group.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Line))
            {
                var depth = element.Section is { } section ? depths.GetValueOrDefault(section, 0) + 1 : 0;
                if (element.Kind == "heading") depths[element.Text] = depth;
                lines.Add($"{new string(' ', depth * 2)}- {Describe(element)} ({element.Path}:{element.Line.ToString(CultureInfo.InvariantCulture)})");
            }
        }

        return lines;
    }

    /// <summary>Every http endpoint defined in <paramref name="paths"/>, grouped by the first segment of its route, with its handler and each line of its signature.</summary>
    public static IReadOnlyList<string> RenderApi(CodeMap map, IReadOnlyCollection<string> paths)
    {
        var lines = new List<string>();
        var groups = Endpoints(map, paths.ToHashSet(StringComparer.Ordinal))
            .GroupBy(e => Prefix(e.Route))
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (lines.Count > 0) lines.Add("");
            lines.AddRange([$"### {group.Key}", ""]);
            foreach (var (entry, symbol, _) in group.OrderBy(e => e.Route, StringComparer.Ordinal).ThenBy(e => e.Entry.Display, StringComparer.Ordinal))
            {
                lines.Add($"- {entry.Display}: {CodeMapQuery.ShortName(symbol)} ({symbol.Path}:{symbol.Range.StartLine.ToString(CultureInfo.InvariantCulture)})");
                lines.AddRange(symbol.Signature.Split('\n').Select(line => $"  - `{line.TrimEnd('\r')}`"));
            }
        }

        return lines;
    }

    /// <summary>The Files section of an architecture or api pack: each member file with the lines the listing above cites in it.</summary>
    public static IReadOnlyList<string> Files(IReadOnlyList<UnitMember> members, ILookup<string, int> lines, string what)
    {
        var files = new List<string>();
        foreach (var path in members.Select(m => m.Path).Distinct(StringComparer.Ordinal))
        {
            var cited = lines[path].Distinct().Order().Select(line => line.ToString(CultureInfo.InvariantCulture)).ToList();
            files.AddRange(["", $"### {path} ({Languages.FromPath(path)})", "",
                cited.Count == 0 ? $"The stored map lists no {what} here." : $"Defines the {what} listed above at line{(cited.Count == 1 ? "" : "s")} {string.Join(", ", cited)}."]);
        }

        return files;
    }

    /// <summary>The http entry points whose symbol is in <paramref name="paths"/>, each once, with its handler and route.</summary>
    public static IReadOnlyList<(EntryPoint Entry, Symbol Symbol, string Route)> Endpoints(CodeMap map, IReadOnlySet<string> paths)
    {
        var symbols = map.Symbols.DistinctBy(s => s.Id, StringComparer.Ordinal).ToDictionary(s => s.Id, StringComparer.Ordinal);
        return map.EntryPoints
            .Distinct()
            .Where(e => e.Kind == "http" && symbols.TryGetValue(e.SymbolId, out var symbol) && paths.Contains(symbol.Path))
            .Select(e => (e, symbols[e.SymbolId], e.Display.IndexOf(' ') is var space and >= 0 ? e.Display[(space + 1)..] : e.Display))
            .ToList();
    }

    private static PlannedUnit Unit(string id, UnitKind kind, string key, ILookup<string, string> structure, IReadOnlyDictionary<string, string> files) =>
        new(id, kind, key, Fidelity.Full, structure
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new UnitMember(id, g.Key, null, Hashing.Sha256Hex(files[g.Key] + "\n" + string.Join('\n', g.Order(StringComparer.Ordinal))), 0))
            .ToList());

    private static string UiLine(UiElement e) => string.Join('\u001f', e.Line.ToString("D8", CultureInfo.InvariantCulture), e.Kind, e.Text, e.Control, e.Target, e.Section, e.Route);

    private static string EndpointLine((EntryPoint Entry, Symbol Symbol, string Route) e) =>
        string.Join('\u001f', e.Entry.Display, e.Symbol.Id, e.Symbol.Range.StartLine.ToString(CultureInfo.InvariantCulture), e.Symbol.Signature);

    private static string Prefix(string route)
    {
        var segment = route.TrimStart('/').Split('/')[0];
        return "/" + segment;
    }

    private static string Describe(UiElement e) => e.Kind switch
    {
        "route" => $"route {e.Text}",
        "nav" => $"nav \"{e.Text}\" -> {e.Target}",
        "control" => e.Text.Length == 0 ? $"control {e.Control} (no label)" : $"control {e.Control} \"{e.Text}\"",
        _ => $"{e.Kind} \"{e.Text}\"",
    };
}
