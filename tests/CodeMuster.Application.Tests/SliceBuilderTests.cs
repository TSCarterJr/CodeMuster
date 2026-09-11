using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class SliceBuilderTests
{
    private static IReadOnlyList<PlannedUnit> Build(CodeMap map, params string[] failedLanguages) =>
        SliceBuilder.Build(new CompositeMap(map, failedLanguages, [Languages.TypeScript]), Included());

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
    public void ASymbolReportedTwice_IsPlannedOnce()
    {
        var map = Map();
        map = map with { Symbols = [.. map.Symbols, map.Symbols.Single(s => s.Id == ArchiveQuote)] };

        var units = Build(map);

        Assert.Single(SliceOf(map, ControllerListQuotes).Members, m => m.Symbol == MoneyFormat);
        Assert.Single(units.Single(u => u.Id == UnitIds.Orphan(ServicePath)).Members, m => m.Symbol == ArchiveQuote);
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

    [Fact]
    public void DeadMethod_LandsInItsFilesOrphan_AsASymbolMember_WhileTheFilesOtherMethodsStayOnlyInSlices()
    {
        var map = Map();

        var units = Build(map);

        var orphan = Assert.Single(units, u => u.Id == UnitIds.Orphan(ServicePath));
        Assert.Equal((UnitKind.Orphan, ServicePath, Fidelity.Full), (orphan.Kind, orphan.Key, orphan.Fidelity));
        var archive = map.Symbols.Single(s => s.Id == ArchiveQuote);
        Assert.Equal(new UnitMember(orphan.Id, ServicePath, ArchiveQuote, archive.BodyHash, 0, archive.Range, archive.Signature), Assert.Single(orphan.Members));
        foreach (var reached in new[] { ServiceListQuotes, ServiceGetQuote, ToSummary })
        {
            Assert.All(units.Where(u => u.Members.Any(m => m.Symbol == reached)), u => Assert.Equal(UnitKind.Slice, u.Kind));
        }

        Assert.DoesNotContain(units, u => u.Id == UnitIds.File(ServicePath));
    }

    [Fact]
    public void FileWithNoReachedSymbol_BecomesAWholeFileOrphan()
    {
        var map = Map();
        map = map with { EntryPoints = map.EntryPoints.Where(e => e.SymbolId != ExecuteAsync).ToList() };

        var orphan = Assert.Single(Build(map), u => u.Id == UnitIds.Orphan(WorkerPath));

        Assert.Equal((UnitKind.Orphan, WorkerPath, Fidelity.Full), (orphan.Kind, orphan.Key, orphan.Fidelity));
        Assert.Equal(new UnitMember(orphan.Id, WorkerPath, null, "content:" + WorkerPath, 0), Assert.Single(orphan.Members));
    }

    [Fact]
    public void FilesWithEveryMethodReached_GetNoUnitOfTheirOwn_AndFilesWithNoSymbols_StayWholeFileUnits()
    {
        var units = Build(Map());

        Assert.Equal(new[] { UnitIds.Orphan(RepositoryPath), UnitIds.Orphan(ServicePath) }, units.Where(u => u.Kind == UnitKind.Orphan).Select(u => u.Id));
        Assert.Equal(RepositoryConstructor, Assert.Single(units.Single(u => u.Id == UnitIds.Orphan(RepositoryPath)).Members).Symbol);
        var files = new[] { ".gitignore", "Directory.Build.props", "MixedRepo.sln", QuotePath, "src/MixedRepo.Api/MixedRepo.Api.csproj", ProgramPath, InterfacePath, BarrelPath, PackageJsonPath, "web/tsconfig.json" };
        Assert.Equal(files.Select(UnitIds.File), units.Where(u => u.Kind == UnitKind.File).Select(u => u.Id));
        Assert.All(units.Where(u => u.Kind == UnitKind.File), u =>
        {
            Assert.Equal(Fidelity.Full, u.Fidelity);
            Assert.Equal(new UnitMember(u.Id, u.Key, null, "content:" + u.Key, 0), Assert.Single(u.Members));
        });
    }

    [Fact]
    public void EveryIncludedFileAndEveryMappedSymbolInOne_BelongsToAtLeastOneUnit()
    {
        var included = Included().Select(f => f.Path).ToList();
        var scenarios = new (CodeMap Map, string[] Failed)[]
        {
            (Map(), []),
            (Map() with { EntryPoints = [] }, []),
            (TypeScript(), [Languages.CSharp]),
        };

        foreach (var (map, failed) in scenarios)
        {
            var units = Build(map, failed);

            var members = units.SelectMany(u => u.Members).ToList();
            Assert.All(included, path => Assert.Contains(members, m => m.Path == path));
            Assert.All(map.Symbols.Where(s => included.Contains(s.Path)), s => Assert.Contains(members, m => m.Symbol == s.Id || (m.Symbol is null && m.Path == s.Path)));
            Assert.All(members, m => Assert.Contains(m.Path, included));
            Assert.All(units, u => Assert.All(u.Members, m => Assert.Equal(u.Id, m.UnitId)));
            Assert.Equal(units.Count, units.Select(u => u.Id).Distinct().Count());
        }
    }

    [Fact]
    public void FailedCSharpMapper_LeavesEveryCsFileAsALowFidelityFileUnit_AndOnlyThose()
    {
        var units = Build(TypeScript(), Languages.CSharp);

        var csharpFiles = Included().Where(f => f.Language == Languages.CSharp).Select(f => UnitIds.File(f.Path));
        Assert.Equal(csharpFiles.Order(StringComparer.Ordinal), units.Where(u => u.Fidelity == Fidelity.Low).Select(u => u.Id).Order(StringComparer.Ordinal));
        Assert.All(units.Where(u => u.Fidelity == Fidelity.Low), u => Assert.Equal(UnitKind.File, u.Kind));
        Assert.Equal(new[] { "/quotes", "/customers" }, units.Where(u => u.Kind == UnitKind.Slice).Select(u => u.Key));
        Assert.DoesNotContain(units, u => u.Kind == UnitKind.Orphan);
    }
}
