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
    public void Call_edges_match_the_golden(string fixture)
    {
        Assert.Equal(
            Sorted(Fixtures.Golden(fixture).Edges.Where(edge => edge.Kind == EdgeKind.Call)),
            Sorted(maps[fixture].Edges.Where(edge => edge.Kind == EdgeKind.Call)));
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
