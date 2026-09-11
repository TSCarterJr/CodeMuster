using System.Text.RegularExpressions;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class GoldenTests
{
    [Fact]
    public void Golden_is_a_consistent_code_map()
    {
        var golden = GoldenAssert.Golden();
        var ids = golden.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(golden.Symbols.Count, ids.Count);
        Assert.All(golden.Symbols, symbol => Assert.StartsWith(symbol.Path + "#", symbol.Id, StringComparison.Ordinal));
        Assert.All(golden.Symbols, symbol => Assert.Matches(new Regex("^[0-9a-f]{64}$"), symbol.BodyHash));
        Assert.All(golden.Edges, edge => Assert.Contains(edge.From, ids));
        Assert.All(golden.Edges, edge => Assert.Contains(edge.To, ids));
        Assert.All(golden.EntryPoints, entry => Assert.Contains(entry.SymbolId, ids));
        Assert.Empty(golden.Diagnostics);
    }
}
