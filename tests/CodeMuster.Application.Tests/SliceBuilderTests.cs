using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class SliceBuilderTests
{
    private static IReadOnlyList<PlannedUnit> Build(CodeMap map) =>
        SliceBuilder.Build(new CompositeMap(map, []), Included());

    private static List<PlannedUnit> Slices(CodeMap map) =>
        Build(map).Where(u => u.Kind == UnitKind.Slice).ToList();

    private static PlannedUnit SliceOf(CodeMap map, string entrySymbolId) =>
        Assert.Single(Build(map), u => u.Id == UnitIds.Slice(entrySymbolId));

    private static IEnumerable<(string? Symbol, int Distance)> Walk(PlannedUnit slice) =>
        slice.Members.Select(m => (m.Symbol, m.Distance)).OrderBy(m => m.Symbol, StringComparer.Ordinal);

    private static IEnumerable<(string? Symbol, int Distance)> Sorted(params (string? Symbol, int Distance)[] members) =>
        members.OrderBy(m => m.Symbol, StringComparer.Ordinal);

    [Fact]
    public void GetQuotesSlice_HoldsExactlyItsCallPath_EachSymbolAtItsShortestDistance()
    {
        var map = Map();

        var slice = SliceOf(map, ControllerListQuotes);

        Assert.Equal(UnitKind.Slice, slice.Kind);
        Assert.Equal("GET /quotes", slice.Key);
        Assert.Equal(Fidelity.Full, slice.Fidelity);
        var symbols = map.Symbols.ToDictionary(s => s.Id);
        var expected = new (string Symbol, int Distance)[] { (ControllerListQuotes, 0), (ServiceListQuotes, 1), (ListForTenant, 2), (ToSummary, 2), (MoneyFormat, 3) }
            .Select(e => new UnitMember(slice.Id, symbols[e.Symbol].Path, e.Symbol, symbols[e.Symbol].BodyHash, e.Distance, symbols[e.Symbol].Range, symbols[e.Symbol].Signature))
            .OrderBy(m => m.Symbol, StringComparer.Ordinal);
        Assert.Equal(expected, slice.Members.OrderBy(m => m.Symbol, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryEntryPoint_GetsOneSlice_AndTheDeadMethodIsInNone()
    {
        var map = Map();

        var slices = Slices(map);

        Assert.Equal(new[] { "GET /quotes", "GET /quotes/{id}", "ReminderWorker", "/quotes", "/customers" }, slices.Select(s => s.Key));
        Assert.DoesNotContain(slices.SelectMany(s => s.Members), m => m.Symbol == ArchiveQuote);
        Assert.Equal(Sorted((ExecuteAsync, 0), (ServiceListQuotes, 1), (ListForTenant, 2), (ToSummary, 2), (MoneyFormat, 3)), Walk(SliceOf(map, ExecuteAsync)));
        Assert.Equal(Sorted((QuotesPage, 0), (UseQuotes, 1), (QuoteTable, 1), (FetchQuotes, 2)), Walk(SliceOf(map, QuotesPage)));
    }

    [Fact]
    public void TwoEntryPointsOnOneSymbol_ProduceOneSlice_KeyedByTheFirst()
    {
        var map = Map();
        map = map with { EntryPoints = [.. map.EntryPoints, new EntryPoint(ControllerListQuotes, "http", "HEAD /quotes")] };

        var slices = Slices(map);

        Assert.Equal(5, slices.Count);
        Assert.Equal("GET /quotes", Assert.Single(slices, s => s.Id == UnitIds.Slice(ControllerListQuotes)).Key);
    }

    [Theory]
    [InlineData(EdgeKind.Call)]
    [InlineData(EdgeKind.Bound)]
    [InlineData(EdgeKind.Implements)]
    [InlineData(EdgeKind.Overrides)]
    public void EveryEdgeKind_IsFollowed(EdgeKind kind)
    {
        var map = Map();
        map = map with { Edges = map.Edges.Select(e => e with { Kind = kind }).ToList() };

        Assert.Equal(5, SliceOf(map, ControllerListQuotes).Members.Count);
    }

    [Fact]
    public void ShortcutsAndCycles_KeepEachSymbolOnce_AtItsShortestDistance()
    {
        var map = Map();
        map = map with
        {
            Edges =
            [
                .. map.Edges,
                new Edge(ControllerListQuotes, MoneyFormat, EdgeKind.Call),
                new Edge(MoneyFormat, ControllerListQuotes, EdgeKind.Call),
                new Edge(ToSummary, ServiceListQuotes, EdgeKind.Call),
            ],
        };

        var slice = SliceOf(map, ControllerListQuotes);

        Assert.Equal(Sorted((ControllerListQuotes, 0), (ServiceListQuotes, 1), (MoneyFormat, 1), (ListForTenant, 2), (ToSummary, 2)), Walk(slice));
    }

    [Fact]
    public void EntryPointWhoseSymbolIsNotInTheMap_GetsNoSlice()
    {
        var map = Map();
        map = map with { EntryPoints = [.. map.EntryPoints, new EntryPoint("M:MixedRepo.Api.Missing.Run", "http", "GET /missing")] };

        Assert.Equal(5, Slices(map).Count);
    }

    [Fact]
    public void SymbolsInFilesThatAreNotIncluded_AreWalkedThrough_ButNeverBecomeMembers()
    {
        var map = Map();
        map = map with
        {
            Edges = [.. map.Edges, new Edge(ServiceListQuotes, MigrationUp, EdgeKind.Call), new Edge(MigrationUp, ArchiveQuote, EdgeKind.Call)],
        };

        var slice = SliceOf(map, ControllerListQuotes);

        Assert.DoesNotContain(slice.Members, m => m.Path == MigrationPath);
        Assert.Equal(3, Assert.Single(slice.Members, m => m.Symbol == ArchiveQuote).Distance);
    }

    [Fact]
    public void EntryPointInAFileThatIsNotIncluded_GetsNoSlice()
    {
        var map = Map();
        map = map with { EntryPoints = [.. map.EntryPoints, new EntryPoint(MigrationUp, "background", "Initial")] };

        Assert.DoesNotContain(Build(map), u => u.Id == UnitIds.Slice(MigrationUp));
    }
}
