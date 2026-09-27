using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class HttpLinksTests
{
    private const string Loader = "web/lib/api.ts#load";

    private static CodeMap Map(IReadOnlyList<EntryPoint> entryPoints, params HttpCall[] calls) =>
        new CodeMap([], [new Edge("a", "b", EdgeKind.Call)], entryPoints, new ResolutionStats(0, 0, []), ["web/a.ts:3: TS1005: ';' expected."]) { HttpCalls = calls };

    private static HttpCall Call(string method, string? url, int line = 7, string from = Loader) =>
        new(from, method, url, url is null ? "endpoint" : $"\"{url}\"", "web/lib/api.ts", line);

    private static EntryPoint Endpoint(string display) => new($"M:Api.{display}", "http", display);

    private static IReadOnlyList<string> Linked(IReadOnlyList<EntryPoint> entryPoints, HttpCall call) =>
        [.. HttpLinks.Join(Map(entryPoints, call)).Map.Edges.Where(edge => edge.Kind == EdgeKind.Http).Select(edge => edge.To)];

    [Theory]
    [InlineData("/{API_URL}/users", "GET /users")]
    [InlineData("/{import.meta.env.VITE_API}/api/orders/{id}", "GET /api/orders/{id}")]
    public void A_leading_variable_is_read_as_the_server_address_when_the_full_path_matches_nothing(string url, string endpoint)
    {
        Assert.Equal([$"M:Api.{endpoint}"], Linked([Endpoint(endpoint)], Call("GET", url)));
    }

    [Fact]
    public void A_leading_variable_that_is_a_real_route_segment_still_matches_as_one()
    {
        Assert.Equal(["M:Api.GET /{tenant}/users"], Linked([Endpoint("GET /{tenant}/users"), Endpoint("GET /users")], Call("GET", "/{tenant}/users")));
    }

    [Theory]
    [InlineData("GET /quotes", "GET", "/quotes", true)]
    [InlineData("GET /quotes", "GET", "/customers", false)]
    [InlineData("GET /quotes/{id}", "GET", "/quotes/5", true)]
    [InlineData("GET /quotes/{id}", "GET", "/quotes/{quote.id}", true)]
    [InlineData("GET /quotes/{id:int}", "GET", "/quotes/{id}", true)]
    [InlineData("GET /quotes/{id:int:min(1)}", "GET", "/quotes/7", true)]
    [InlineData("GET /quotes/{id?}", "GET", "/quotes", true)]
    [InlineData("GET /quotes/{id?}", "GET", "/quotes/3", true)]
    [InlineData("GET /quotes/{id=1}", "GET", "/quotes", true)]
    [InlineData("GET /quotes/{id}", "GET", "/quotes", false)]
    [InlineData("GET /quotes/{id}", "GET", "/quotes/3/lines", false)]
    [InlineData("GET /files/{*path}", "GET", "/files/a/b/c", true)]
    [InlineData("GET /files/{**path}", "GET", "/files", true)]
    [InlineData("GET /files/{*path}", "GET", "/other/a", false)]
    [InlineData("GET /api/{tenant}/quotes", "GET", "/api/acme/quotes", true)]
    [InlineData("GET /Api/Quotes", "GET", "/api/QUOTES", true)]
    [InlineData("GET /quotes", "GET", "/quotes/", true)]
    [InlineData("GET /quotes/", "GET", "quotes", true)]
    [InlineData("GET /", "GET", "/", true)]
    [InlineData("GET /quotes", "GET", "/quotes/{id}", false)]
    [InlineData("GET /quotes/latest", "GET", "/quotes/{id}", false)]
    [InlineData("GET /quotes/{id}", "GET", "/quotes/{id}{suffix}", true)]
    [InlineData("POST /quotes", "GET", "/quotes", false)]
    [InlineData("post /quotes", "POST", "/quotes", true)]
    [InlineData("ANY /quotes", "DELETE", "/quotes", true)]
    [InlineData("PUT /quotes", "ANY", "/quotes", true)]
    public void A_call_links_to_an_endpoint_by_method_and_route_segments(string endpoint, string method, string url, bool links)
    {
        var linked = Linked([Endpoint(endpoint)], Call(method, url));

        Assert.Equal(links ? [$"M:Api.{endpoint}"] : [], linked);
    }

    [Fact]
    public void A_literal_route_wins_over_a_parameter_and_an_explicit_method_over_any()
    {
        EntryPoint[] endpoints = [Endpoint("GET /quotes/{id}"), Endpoint("GET /quotes/latest"), Endpoint("ANY /quotes/latest")];

        Assert.Equal(["M:Api.GET /quotes/latest"], Linked(endpoints, Call("GET", "/quotes/latest")));
        Assert.Equal(["M:Api.GET /quotes/{id}"], Linked(endpoints, Call("GET", "/quotes/42")));
        Assert.Equal(["M:Api.ANY /quotes/latest"], Linked(endpoints, Call("POST", "/quotes/latest")));
    }

    [Fact]
    public void Equally_specific_endpoints_are_all_linked_and_repeated_calls_add_one_edge()
    {
        EntryPoint[] endpoints = [new("M:Api.A", "http", "GET /quotes"), new("M:Api.B", "http", "GET /Quotes")];

        var joined = HttpLinks.Join(Map(endpoints, Call("GET", "/quotes", 7), Call("GET", "/quotes", 9)));

        Assert.Equal(
            [new Edge("a", "b", EdgeKind.Call), new Edge(Loader, "M:Api.A", EdgeKind.Http), new Edge(Loader, "M:Api.B", EdgeKind.Http)],
            joined.Map.Edges);
        Assert.Equal((2, 0, 0, 0), (joined.Linked, joined.Unmatched, joined.Unresolved, joined.Uncalled));
    }

    [Fact]
    public void Unmatched_and_runtime_calls_and_uncalled_endpoints_are_diagnostics_after_the_mappers()
    {
        EntryPoint[] endpoints =
        [
            Endpoint("GET /quotes"),
            Endpoint("GET /quotes/{id}"),
            new("M:Api.Worker", "background", "ReminderWorker"),
            new("web/app/quotes/page.tsx#QuotesPage", "page", "/quotes"),
        ];

        var joined = HttpLinks.Join(Map(endpoints, Call("GET", "/quotes", 9), Call("GET", "/customers", 14), Call("ANY", null, 20)));

        Assert.Equal(
            [
                "web/a.ts:3: TS1005: ';' expected.",
                "http: web/lib/api.ts:14 GET /customers matches no endpoint",
                "http: web/lib/api.ts:20 ANY URL built at runtime: endpoint",
                "http: GET /quotes/{id} is not called from the mapped UI",
            ],
            joined.Map.Diagnostics);
        Assert.Equal((1, 1, 1, 1), (joined.Linked, joined.Unmatched, joined.Unresolved, joined.Uncalled));
        Assert.Equal("linked 1 UI call to an endpoint; 2 calls and 1 endpoint unmatched", joined.Summary);
    }

    [Fact]
    public void Without_any_http_call_no_endpoint_is_reported_uncalled_and_there_is_no_summary()
    {
        var map = Map([Endpoint("GET /quotes"), Endpoint("POST /quotes")]);

        var joined = HttpLinks.Join(map);

        Assert.Equal(map.Edges, joined.Map.Edges);
        Assert.Equal(map.Diagnostics, joined.Map.Diagnostics);
        Assert.Null(joined.Summary);
    }

    [Fact]
    public void Only_runtime_calls_still_report_every_uncalled_endpoint()
    {
        var joined = HttpLinks.Join(Map([Endpoint("GET /quotes")], Call("GET", null)));

        Assert.Equal(
            ["web/a.ts:3: TS1005: ';' expected.", "http: web/lib/api.ts:7 GET URL built at runtime: endpoint", "http: GET /quotes is not called from the mapped UI"],
            joined.Map.Diagnostics);
        Assert.Equal("linked 0 UI calls to an endpoint; 1 call and 1 endpoint unmatched", joined.Summary);
    }

    [Fact]
    public void An_http_entry_point_without_a_method_and_route_is_never_linked_or_reported()
    {
        var joined = HttpLinks.Join(Map([new("M:Api.Odd", "http", "GET")], Call("GET", "/quotes")));

        Assert.DoesNotContain(joined.Map.Edges, edge => edge.Kind == EdgeKind.Http);
        Assert.DoesNotContain(joined.Map.Diagnostics, diagnostic => diagnostic.Contains("is not called", StringComparison.Ordinal));
    }
}
