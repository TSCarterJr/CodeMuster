using System.Text.Json;

namespace CodeMuster.Domain.Tests;

public class CodeMapJsonTests
{
    private static CodeMap SmallMap() => new(
        Symbols:
        [
            new Symbol("M:Api.QuotesController.Get(System.Int32)", "src/QuotesController.cs", new LineRange(10, 20), "method", "public sealed class QuotesController\n[HttpGet] public Quote Get(int id)", "a1"),
            new Symbol("M:Api.QuoteService.Get(System.Int32)", "src/QuoteService.cs", new LineRange(5, 12), "method", "public sealed class QuoteService : IQuotes\npublic Quote Get(int id)", "b2"),
            new Symbol("M:Api.Repo.Load(System.Int32)", "src/Repo.cs", new LineRange(1, 8), "method", "public sealed class Repo\npublic Quote Load(int id)", "c3"),
        ],
        Edges:
        [
            new Edge("M:Api.QuotesController.Get(System.Int32)", "M:Api.QuoteService.Get(System.Int32)", EdgeKind.Bound),
            new Edge("M:Api.QuoteService.Get(System.Int32)", "M:Api.Repo.Load(System.Int32)", EdgeKind.Call),
        ],
        EntryPoints: [new EntryPoint("M:Api.QuotesController.Get(System.Int32)", "http", "GET /quotes/{id}")],
        Resolution: new ResolutionStats(5, 1, ["Logger.Log"]),
        Diagnostics: ["project.assets.json not found; run dotnet restore"]);

    [Fact]
    public void Round_trips_a_small_map()
    {
        var map = SmallMap();

        var parsed = CodeMapJson.Parse(CodeMapJson.Serialize(map));

        Assert.Equal(map.Symbols, parsed.Symbols);
        Assert.Equal(map.Edges, parsed.Edges);
        Assert.Equal(map.EntryPoints, parsed.EntryPoints);
        Assert.Equal(map.Resolution.Resolved, parsed.Resolution.Resolved);
        Assert.Equal(map.Resolution.Unresolved, parsed.Resolution.Unresolved);
        Assert.Equal(map.Resolution.TopUnresolvedNames, parsed.Resolution.TopUnresolvedNames);
        Assert.Equal(map.Diagnostics, parsed.Diagnostics);
    }

    [Fact]
    public void A_normalized_hash_is_read_when_present_and_left_out_when_absent()
    {
        const string json = """{"symbols":[{"id":"a","path":"a.ts","range":{"start_line":1,"end_line":2},"kind":"function","signature":"f()","body_hash":"b","normalized_hash":"n"},{"id":"c","path":"c.ts","range":{"start_line":1,"end_line":2},"kind":"function","signature":"g()","body_hash":"d"}],"edges":[],"entry_points":[],"resolution":{"resolved":0,"unresolved":0,"top_unresolved_names":[]},"diagnostics":[]}""";

        var map = CodeMapJson.Parse(json);

        Assert.Equal("n", map.Symbols[0].NormalizedHash);
        Assert.Null(map.Symbols[1].NormalizedHash);
        Assert.DoesNotContain("normalized_hash\": null", CodeMapJson.Serialize(map));
        Assert.Contains("\"normalized_hash\": \"n\"", CodeMapJson.Serialize(map));
    }

    [Fact]
    public void Serializes_with_snake_case_names_and_lowercase_enums()
    {
        var json = CodeMapJson.Serialize(SmallMap());

        Assert.Contains("\"body_hash\"", json);
        Assert.Contains("\"entry_points\"", json);
        Assert.Contains("\"top_unresolved_names\"", json);
        Assert.Contains("\"kind\": \"bound\"", json);
        Assert.Contains("\"diagnostics\"", json);
    }

    [Fact]
    public void Ui_elements_round_trip_with_their_optional_fields_left_out_when_absent()
    {
        var map = SmallMap() with
        {
            UiElements =
            [
                new UiElement("route", "/settings", "web/app/settings/page.tsx", 3, Route: "/settings"),
                new UiElement("control", "Auto charge customer", "web/app/settings/page.tsx", 12, Control: "Switch", Section: "General", Route: "/settings"),
                new UiElement("nav", "Invoices", "web/components/Sidebar.tsx", 4, Target: "/invoices"),
            ],
        };

        var json = CodeMapJson.Serialize(map);
        var parsed = CodeMapJson.Parse(json);

        Assert.Equal(map.UiElements, parsed.UiElements);
        Assert.Contains("\"ui_elements\"", json);
        Assert.DoesNotContain("null", json.Split("\"ui_elements\"")[1]);
    }

    [Fact]
    public void A_map_without_ui_elements_leaves_the_key_out_and_parses_with_none()
    {
        var json = CodeMapJson.Serialize(SmallMap());

        Assert.DoesNotContain("ui_elements", json);
        Assert.Empty(CodeMapJson.Parse(json).UiElements);
    }

    [Fact]
    public void Round_trips_http_calls_including_one_built_at_runtime()
    {
        var map = SmallMap() with
        {
            HttpCalls =
            [
                new HttpCall("web/lib/api.ts#fetchQuotes", "GET", "/quotes/{id}", "`/quotes/${id}`", "web/lib/api.ts", 9),
                new HttpCall("web/lib/api.ts#save", "ANY", null, "endpoint", "web/lib/api.ts", 14),
            ],
        };

        var json = CodeMapJson.Serialize(map);
        var parsed = CodeMapJson.Parse(json);

        Assert.Contains("\"http_calls\"", json);
        Assert.Contains("\"url\": null", json);
        Assert.Equal(map.HttpCalls, parsed.HttpCalls);
    }

    [Fact]
    public void A_map_without_http_calls_parses_with_none()
    {
        const string json = """
            {
              "symbols": [],
              "edges": [],
              "entry_points": [],
              "resolution": { "resolved": 0, "unresolved": 0, "top_unresolved_names": [] },
              "diagnostics": []
            }
            """;

        Assert.Empty(CodeMapJson.Parse(json).HttpCalls);
    }

    [Fact]
    public void Http_edges_serialize_as_lowercase_http()
    {
        var map = SmallMap() with { Edges = [new Edge("web/lib/api.ts#fetchQuotes", "M:Api.QuotesController.Get(System.Int32)", EdgeKind.Http)] };

        var json = CodeMapJson.Serialize(map);

        Assert.Contains("\"kind\": \"http\"", json);
        Assert.Equal(map.Edges, CodeMapJson.Parse(json).Edges);
    }

    [Fact]
    public void Missing_required_field_throws()
    {
        const string json = """
            {
              "symbols": [
                { "id": "a", "path": "a.cs", "range": { "start_line": 1, "end_line": 2 }, "kind": "method", "body_hash": "x" }
              ],
              "edges": [],
              "entry_points": [],
              "resolution": { "resolved": 0, "unresolved": 0, "top_unresolved_names": [] },
              "diagnostics": []
            }
            """;

        Assert.Throws<JsonException>(() => CodeMapJson.Parse(json));
    }

    [Fact]
    public void Null_document_throws()
    {
        var error = Assert.Throws<JsonException>(() => CodeMapJson.Parse("null"));

        Assert.Equal("code map is null", error.Message);
    }

    [Fact]
    public void Round_trips_an_empty_map()
    {
        var map = new CodeMap([], [], [], new ResolutionStats(0, 0, []), []);

        var parsed = CodeMapJson.Parse(CodeMapJson.Serialize(map));

        Assert.Empty(parsed.Symbols);
        Assert.Empty(parsed.Edges);
        Assert.Empty(parsed.EntryPoints);
        Assert.Equal(0, parsed.Resolution.Resolved);
        Assert.Equal(0, parsed.Resolution.Unresolved);
        Assert.Empty(parsed.Resolution.TopUnresolvedNames);
        Assert.Empty(parsed.Diagnostics);
    }
}
