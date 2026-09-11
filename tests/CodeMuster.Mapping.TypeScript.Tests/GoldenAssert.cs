using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

internal static class GoldenAssert
{
    public static CodeMap Golden() => CodeMapJson.Parse(File.ReadAllText(TestPaths.Golden));

    public static void Matches(CodeMap actual)
    {
        var golden = Golden();

        Assert.Equal(Sorted(golden.Symbols), Sorted(actual.Symbols));
        Assert.Equal(Sorted(golden.Edges), Sorted(actual.Edges));
        Assert.Equal(Sorted(golden.EntryPoints), Sorted(actual.EntryPoints));
        Assert.Equal(golden.Resolution.Resolved, actual.Resolution.Resolved);
        Assert.Equal(golden.Resolution.Unresolved, actual.Resolution.Unresolved);
        Assert.Equal(golden.Resolution.TopUnresolvedNames, actual.Resolution.TopUnresolvedNames);
        Assert.Empty(actual.Diagnostics);
    }

    public static IEnumerable<Symbol> Sorted(IEnumerable<Symbol> symbols) =>
        symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal);

    public static IEnumerable<Edge> Sorted(IEnumerable<Edge> edges) =>
        edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Kind);

    public static IEnumerable<EntryPoint> Sorted(IEnumerable<EntryPoint> entryPoints) =>
        entryPoints.OrderBy(entry => entry.SymbolId, StringComparer.Ordinal).ThenBy(entry => entry.Display, StringComparer.Ordinal);
}
