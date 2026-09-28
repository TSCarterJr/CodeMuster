using System.Text.RegularExpressions;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class GoldenTests
{
    [Theory]
    [InlineData("mixed-repo")]
    [InlineData("express-js")]
    public void Golden_is_a_consistent_code_map(string fixture)
    {
        var golden = GoldenAssert.Golden(fixture);
        var ids = golden.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(golden.Symbols.Count, ids.Count);
        Assert.All(golden.Symbols, symbol => Assert.StartsWith(symbol.Path + "#", symbol.Id, StringComparison.Ordinal));
        Assert.All(golden.Symbols, symbol => Assert.Matches(new Regex("^[0-9a-f]{64}$"), symbol.BodyHash));
        Assert.All(golden.Symbols, symbol => Assert.Matches(new Regex("^[0-9a-f]{64}$"), symbol.NormalizedHash));
        Assert.All(golden.Edges, edge => Assert.Contains(edge.From, ids));
        Assert.All(golden.Edges, edge => Assert.Contains(edge.To, ids));
        Assert.All(golden.EntryPoints, entry => Assert.Contains(entry.SymbolId, ids));
        Assert.NotEmpty(golden.HttpCalls);
        Assert.All(golden.HttpCalls, call => Assert.Contains(call.From, ids));
        Assert.All(golden.HttpCalls, call => Assert.StartsWith(call.Path + "#", call.From, StringComparison.Ordinal));
        Assert.Empty(golden.Diagnostics);
        Assert.NotEmpty(golden.Declarations);
        Assert.Equal(golden.Declarations.Count, golden.Declarations.Select(declaration => declaration.Id).Distinct().Count());
        Assert.All(golden.Declarations, declaration => Assert.DoesNotContain(declaration.Id, ids));
        Assert.All(golden.Declarations, declaration => Assert.StartsWith(declaration.Path + "#", declaration.Id, StringComparison.Ordinal));
        Assert.All(golden.Declarations, declaration => Assert.Matches(new Regex("^[0-9a-f]{64}$"), declaration.BodyHash));
        var targets = ids.Concat(golden.Declarations.Select(declaration => declaration.Id)).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(golden.References);
        Assert.All(golden.References, reference => Assert.Contains(reference.To, targets));
        Assert.All(golden.References, reference => Assert.True(reference.From == reference.Path || (targets.Contains(reference.From) && reference.From.StartsWith(reference.Path + "#", StringComparison.Ordinal)), reference.From));
        Assert.Equal(
            golden.References.OrderBy(reference => reference.Path, StringComparer.Ordinal).ThenBy(reference => reference.Line).ThenBy(reference => reference.Column)
                .ThenBy(reference => reference.From, StringComparer.Ordinal).ThenBy(reference => reference.To, StringComparer.Ordinal).ThenBy(reference => reference.Kind),
            golden.References);
    }
}
