namespace CodeMuster.Mapping.CSharp.Tests;

public class GoldenTests
{
    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Edges_and_entry_points_point_at_golden_symbols(string fixture)
    {
        var golden = Fixtures.Golden(fixture);
        var ids = golden.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(golden.Symbols.Count, ids.Count);
        Assert.All(golden.Edges, edge =>
        {
            Assert.Contains(edge.From, ids);
            Assert.Contains(edge.To, ids);
        });
        Assert.All(golden.EntryPoints, entry => Assert.Contains(entry.SymbolId, ids));
    }

    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("minimal-api")]
    public void Ranges_start_at_the_declaration_and_end_at_its_closing_brace(string fixture)
    {
        foreach (var symbol in Fixtures.Golden(fixture).Symbols)
        {
            var lines = File.ReadAllLines(Path.Combine(Fixtures.Root(fixture), symbol.Path));
            var member = symbol.Signature[(symbol.Signature.IndexOf('\n') + 1)..];

            Assert.StartsWith(lines[symbol.Range.StartLine - 1].Trim(), member);
            Assert.Equal("}", lines[symbol.Range.EndLine - 1].Trim());
        }
    }
}
