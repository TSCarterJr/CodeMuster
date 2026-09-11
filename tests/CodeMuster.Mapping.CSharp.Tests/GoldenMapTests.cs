using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class GoldenMapTests(FixtureMaps maps) : IClassFixture<FixtureMaps>
{
    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Symbols_match_the_golden(string fixture)
    {
        Assert.Equal(
            Fixtures.Golden(fixture).Symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal),
            maps[fixture].Symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Edges_match_the_golden(string fixture)
    {
        Assert.Equal(Sorted(Fixtures.Golden(fixture).Edges), Sorted(maps[fixture].Edges));
    }

    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Entry_points_match_the_golden(string fixture)
    {
        Assert.Equal(
            Fixtures.Golden(fixture).EntryPoints.OrderBy(entry => entry.Display, StringComparer.Ordinal),
            maps[fixture].EntryPoints.OrderBy(entry => entry.Display, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Resolution_matches_the_golden_with_no_diagnostics(string fixture)
    {
        var golden = Fixtures.Golden(fixture);
        var map = maps[fixture];

        Assert.Equal(golden.Resolution.Resolved, map.Resolution.Resolved);
        Assert.Equal(golden.Resolution.Unresolved, map.Resolution.Unresolved);
        Assert.Equal(golden.Resolution.TopUnresolvedNames, map.Resolution.TopUnresolvedNames);
        Assert.Equal(golden.Diagnostics, map.Diagnostics);
    }

    [Fact]
    public void IQuoteService_calls_bind_to_QuoteService_only()
    {
        var edges = maps["mixed-repo"].Edges;

        Assert.DoesNotContain(edges, edge => edge.To.Contains(".EmptyQuoteService.", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "M:MixedRepo.Api.Services.QuoteService.GetQuote(System.Int32,System.Int32)",
                "M:MixedRepo.Api.Services.QuoteService.ListQuotes(System.Int32)",
                "M:MixedRepo.Api.Services.QuoteService.ListQuotes(System.Int32)",
            },
            edges.Where(edge => edge.Kind == EdgeKind.Bound).Select(edge => edge.To).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Body_hash_is_sha256_of_the_declaration_tokens_joined_by_single_spaces()
    {
        var symbol = maps["mixed-repo"].Symbols.Single(symbol => symbol.Id == "M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32)");

        Assert.Equal(
            Hashing.Sha256Hex("[ HttpGet ( \"quotes\" ) ] public IReadOnlyList < QuoteSummary > ListQuotes ( int tenantId ) { return quotes . ListQuotes ( tenantId ) ; }"),
            symbol.BodyHash);
    }

    private static IEnumerable<Edge> Sorted(IEnumerable<Edge> edges) =>
        edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Kind);
}
