using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// Joins the UI's static HTTP calls to the C# <c>http</c> entry points that serve them (D61). A call links to an endpoint when the methods agree (ANY on either side agrees with any method)
/// and the routes match segment by segment, empty segments ignored: a route parameter (<c>{id}</c>, <c>{id:int}</c>) matches any one segment, an optional one (<c>{id?}</c>, <c>{id=1}</c>) may also match none,
/// a catch-all (<c>{*rest}</c>) matches the remainder, a call's own parameter segment matches only a route parameter, and literal segments compare case-insensitively as ASP.NET routing does.
/// Of the endpoints a call matches it links to those with the most literal segments, then those that name a method, as ASP.NET's route precedence would choose.
/// </summary>
public static class HttpLinks
{
    /// <summary>Adds one <see cref="EdgeKind.Http"/> edge per calling symbol and endpoint it reaches, then, after the mappers' diagnostics, one diagnostic per call that matches no endpoint,
    /// per call whose URL is built at runtime and, when at least one call was found, per endpoint no call reaches. Symbols, entry points and every other edge are unchanged.</summary>
    public static HttpLinkResult Join(CodeMap map)
    {
        var endpoints = map.EntryPoints
            .Where(entry => entry.Kind == "http")
            .Select(Endpoint.Parse)
            .OfType<Endpoint>()
            .ToList();
        var edges = new List<Edge>();
        var linked = new HashSet<Edge>();
        var reached = new HashSet<Endpoint>();
        var diagnostics = new List<string>();
        var (linkedCalls, unmatched, unresolved) = (0, 0, 0);
        foreach (var call in map.HttpCalls)
        {
            if (call.Url is null)
            {
                unresolved++;
                diagnostics.Add(string.Create(CultureInfo.InvariantCulture, $"{HttpCall.DiagnosticPrefix}{call.Path}:{call.Line} {call.Method} URL built at runtime: {call.Text}"));
                continue;
            }

            var targets = Best(endpoints.Where(endpoint => endpoint.Serves(call.Method, Segments(call.Url))).ToList());
            if (targets.Count == 0)
            {
                unmatched++;
                diagnostics.Add(string.Create(CultureInfo.InvariantCulture, $"{HttpCall.DiagnosticPrefix}{call.Path}:{call.Line} {call.Method} {call.Url} matches no endpoint"));
                continue;
            }

            linkedCalls++;
            foreach (var target in targets)
            {
                reached.Add(target);
                var edge = new Edge(call.From, target.Entry.SymbolId, EdgeKind.Http);
                if (linked.Add(edge))
                {
                    edges.Add(edge);
                }
            }
        }

        var uncalled = map.HttpCalls.Count == 0 ? [] : endpoints.Where(endpoint => !reached.Contains(endpoint)).ToList();
        diagnostics.AddRange(uncalled.Select(endpoint => $"{HttpCall.DiagnosticPrefix}{endpoint.Entry.Display} is not called from the mapped UI"));
        var joined = map with { Edges = [.. map.Edges, .. edges], Diagnostics = [.. map.Diagnostics, .. diagnostics] };
        return new HttpLinkResult(joined, linkedCalls, unmatched, unresolved, uncalled.Count, map.HttpCalls.Count > 0);
    }

    private static List<Endpoint> Best(List<Endpoint> matches) =>
        [.. matches
            .GroupBy(endpoint => (endpoint.Literals, endpoint.NamesMethod))
            .OrderByDescending(group => group.Key.Literals)
            .ThenByDescending(group => group.Key.NamesMethod)
            .Take(1)
            .SelectMany(group => group)];

    private static string[] Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsParameter(string segment) => segment.Contains('{', StringComparison.Ordinal);

    private sealed record Endpoint(EntryPoint Entry, string Method, string[] Route)
    {
        public int Literals => Route.Count(segment => !IsParameter(segment));

        public bool NamesMethod => Method != "ANY";

        public static Endpoint? Parse(EntryPoint entry) =>
            entry.Display.Split(' ', 2) is [var method, var route] && route.StartsWith('/')
                ? new Endpoint(entry, method.ToUpperInvariant(), Segments(route))
                : null;

        public bool Serves(string method, string[] call) =>
            (!NamesMethod || method == "ANY" || string.Equals(method, Method, StringComparison.OrdinalIgnoreCase)) && Matches(0, call, 0);

        private bool Matches(int routeIndex, string[] call, int callIndex)
        {
            if (routeIndex == Route.Length)
            {
                return callIndex == call.Length;
            }

            var segment = Route[routeIndex];
            if (segment.StartsWith("{*", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsParameter(segment))
            {
                var optional = segment.EndsWith("?}", StringComparison.Ordinal) || segment.Contains('=', StringComparison.Ordinal);
                return (callIndex < call.Length && Matches(routeIndex + 1, call, callIndex + 1)) || (optional && Matches(routeIndex + 1, call, callIndex));
            }

            return callIndex < call.Length
                && !IsParameter(call[callIndex])
                && string.Equals(segment, call[callIndex], StringComparison.OrdinalIgnoreCase)
                && Matches(routeIndex + 1, call, callIndex + 1);
        }
    }
}

/// <summary>The code map with the UI-to-API join applied, and what the join found (D61).</summary>
/// <param name="Map">The input map with <see cref="EdgeKind.Http"/> edges and the join's diagnostics appended.</param>
/// <param name="Linked">Calls linked to at least one endpoint.</param>
/// <param name="Unmatched">Calls with a static URL that match no endpoint.</param>
/// <param name="Unresolved">Calls whose URL is built at runtime.</param>
/// <param name="Uncalled">Endpoints no call reaches; zero when no call was found.</param>
/// <param name="FoundCalls">True when the mappers found at least one HTTP call.</param>
public sealed record HttpLinkResult(CodeMap Map, int Linked, int Unmatched, int Unresolved, int Uncalled, bool FoundCalls)
{
    /// <summary>One progress line for scan, such as "linked 1 UI call to an endpoint; 1 call and 1 endpoint unmatched"; null when no HTTP call was found.</summary>
    public string? Summary => FoundCalls
        ? string.Create(CultureInfo.InvariantCulture, $"linked {Linked} UI {Plural(Linked, "call")} to an endpoint; {Unmatched + Unresolved} {Plural(Unmatched + Unresolved, "call")} and {Uncalled} {Plural(Uncalled, "endpoint")} unmatched")
        : null;

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";
}
