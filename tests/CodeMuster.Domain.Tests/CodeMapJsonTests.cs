using System.Text.Json;

namespace CodeMuster.Domain.Tests;

public class CodeMapJsonTests
{
    private static CodeMap SmallMap() => new(
        Symbols:
        [
            new Symbol("src/Quotes.cs:QuoteService.Get", "src/Quotes.cs", new LineRange(10, 20), "method", "Quote Get(int id)", "a1"),
            new Symbol("src/Quotes.cs:QuoteService", "src/Quotes.cs", new LineRange(5, 30), "class", "class QuoteService : IQuotes", "b2"),
            new Symbol("src/IQuotes.cs:IQuotes", "src/IQuotes.cs", new LineRange(1, 8), "interface", "interface IQuotes", "c3"),
        ],
        Edges:
        [
            new Edge("src/Quotes.cs:QuoteService.Get", "src/Repo.cs:Repo.Load", EdgeKind.Call),
            new Edge("src/Quotes.cs:QuoteService", "src/IQuotes.cs:IQuotes", EdgeKind.Implements),
        ],
        EntryPoints: [new EntryPoint("src/Quotes.cs:QuoteService.Get", "http", "GET /quotes")],
        Resolution: new ResolutionStats(5, 1, ["Logger.Log"]));

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
    }

    [Fact]
    public void Serializes_with_snake_case_names_and_lowercase_enums()
    {
        var json = CodeMapJson.Serialize(SmallMap());

        Assert.Contains("\"body_hash\"", json);
        Assert.Contains("\"entry_points\"", json);
        Assert.Contains("\"top_unresolved_names\"", json);
        Assert.Contains("\"kind\": \"implements\"", json);
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
              "resolution": { "resolved": 0, "unresolved": 0, "top_unresolved_names": [] }
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
        var map = new CodeMap([], [], [], new ResolutionStats(0, 0, []));

        var parsed = CodeMapJson.Parse(CodeMapJson.Serialize(map));

        Assert.Empty(parsed.Symbols);
        Assert.Empty(parsed.Edges);
        Assert.Empty(parsed.EntryPoints);
        Assert.Equal(0, parsed.Resolution.Resolved);
        Assert.Equal(0, parsed.Resolution.Unresolved);
        Assert.Empty(parsed.Resolution.TopUnresolvedNames);
    }
}
