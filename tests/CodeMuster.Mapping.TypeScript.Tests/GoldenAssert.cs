using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

internal static class GoldenAssert
{
    public static CodeMap Golden(string fixture = "mixed-repo") => CodeMapJson.Parse(File.ReadAllText(TestPaths.GoldenFor(fixture)));

    public static void Matches(CodeMap actual, string fixture = "mixed-repo")
    {
        var golden = Golden(fixture);

        Assert.Equal(Sorted(golden.Symbols), Sorted(actual.Symbols));
        Assert.Equal(Sorted(golden.Edges), Sorted(actual.Edges));
        Assert.Equal(Sorted(golden.EntryPoints), Sorted(actual.EntryPoints));
        Assert.Equal(golden.Resolution.Resolved, actual.Resolution.Resolved);
        Assert.Equal(golden.Resolution.Unresolved, actual.Resolution.Unresolved);
        Assert.Equal(golden.Resolution.TopUnresolvedNames, actual.Resolution.TopUnresolvedNames);
        Assert.Empty(actual.Diagnostics);
        Assert.Equal(Sorted(golden.HttpCalls), Sorted(actual.HttpCalls));
    }

    public static IEnumerable<Symbol> Sorted(IEnumerable<Symbol> symbols) =>
        symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal);

    public static IEnumerable<Edge> Sorted(IEnumerable<Edge> edges) =>
        edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Kind);

    public static IEnumerable<HttpCall> Sorted(IEnumerable<HttpCall> calls) =>
        calls.OrderBy(call => call.Path, StringComparer.Ordinal).ThenBy(call => call.Line).ThenBy(call => call.Text, StringComparer.Ordinal);

    public static IEnumerable<EntryPoint> Sorted(IEnumerable<EntryPoint> entryPoints) =>
        entryPoints.OrderBy(entry => entry.SymbolId, StringComparer.Ordinal).ThenBy(entry => entry.Display, StringComparer.Ordinal);
}
