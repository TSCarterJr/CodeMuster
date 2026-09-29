using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class DuplicateTests
{
    private const string Root = "/repos/mixed-repo";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, CSharp());
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, TypeScript());

    public DuplicateTests() => AddTo(tree);

    private static Symbol At(string id, string path, int start, int end, string? hash) =>
        new(id, path, new LineRange(start, end), "method", "sig " + id, "body:" + id, hash);

    private static FileRecord Included(string path) => new(path, Languages.FromPath(path), "content:" + path, 1, "", "", "", null, null, null, null, null, null);

    private static CodeMap MapOf(params Symbol[] symbols) => new(symbols, [], [], new ResolutionStats(0, 0, []), []);

    private Task<ScanResult> ScanAsync(Config? config = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Config.Default, [csharp, typescript], Root).RunAsync(CancellationToken.None);

    private void DuplicateGetQuoteAndTheWorkerLoop()
    {
        csharp.Map = CSharp() with
        {
            Symbols = CSharp().Symbols
                .Select(s => s.Id == ServiceGetQuote ? s with { Range = new LineRange(13, 18), NormalizedHash = "norm:lookup" } : s)
                .Select(s => s.Id == ExecuteAsync ? s with { NormalizedHash = "norm:lookup" } : s)
                .ToList(),
        };
    }

    [Fact]
    public void Groups_NeedTwoCopiesOfAtLeastSixLines_InOneLanguage_InIncludedFiles()
    {
        var map = MapOf(
            At("M:A.Six", "src/A.cs", 10, 15, "h1"),
            At("M:B.Eight", "src/B.cs", 1, 8, "h1"),
            At("M:C.Five", "src/C.cs", 1, 5, "h1"),
            At("web/a.ts#six", "web/a.ts", 1, 6, "h1"),
            At("web/b.ts#one", "web/b.ts", 1, 6, "h2"),
            At("web/c.ts#two", "web/c.ts", 3, 9, "h2"),
            At("M:Gen.Copy", "src/Gen.g.cs", 1, 20, "h2"),
            At("M:D.NoHash", "src/D.cs", 1, 20, null),
            At("M:E.NoHash", "src/E.cs", 1, 20, null));
        string[] included = ["src/A.cs", "src/B.cs", "src/C.cs", "web/a.ts", "web/b.ts", "web/c.ts", "src/D.cs", "src/E.cs"];

        var units = DuplicateReview.Plan(map, included.Select(Included).ToList());

        Assert.Equal(["duplicate:h1", "duplicate:h2"], units.Select(u => u.Id));
        Assert.Equal(["M:A.Six", "M:B.Eight"], units[0].Members.Select(m => m.Symbol));
        Assert.Equal(["web/b.ts#one", "web/c.ts#two"], units[1].Members.Select(m => m.Symbol));
        Assert.All(units, u => Assert.Equal(UnitKind.Duplicate, u.Kind));
        Assert.All(units.SelectMany(u => u.Members), m => Assert.Equal((0, SliceBuilder.MemberHash(map.Symbols.Single(s => s.Id == m.Symbol))), (m.Distance, m.MemberHash)));
        Assert.Equal("A.Six and 1 other copy", units[0].Key);
    }

    [Fact]
    public void AHashInTwoLanguages_MakesTwoGroups_NeverOneAcrossLanguages()
    {
        var map = MapOf(
            At("M:A.One", "src/A.cs", 1, 6, "same"),
            At("M:B.Two", "src/B.cs", 1, 6, "same"),
            At("web/a.ts#one", "web/a.ts", 1, 6, "same"),
            At("web/b.js#two", "web/b.js", 1, 6, "same"),
            At("web/c.ts#three", "web/c.ts", 1, 6, "same"));

        var units = DuplicateReview.Plan(map, new[] { "src/A.cs", "src/B.cs", "web/a.ts", "web/b.js", "web/c.ts" }.Select(Included).ToList());

        Assert.Equal(["duplicate:same:csharp", "duplicate:same:typescript"], units.Select(u => u.Id));
        Assert.Equal(["web/a.ts#one", "web/c.ts#three"], units[1].Members.Select(m => m.Symbol));
    }

    [Fact]
    public void AGroupIsCappedAtTwelveCopies_ByPathAndLine_AndItsIdAndFingerprintDoNotDependOnMapOrder()
    {
        var copies = Enumerable.Range(0, 14).Select(i => At($"M:C{i:00}.Copy", $"src/C{i:00}.cs", 1, 10, "wide")).ToList();

        var units = DuplicateReview.Plan(MapOf([.. copies]), copies.Select(c => Included(c.Path)).ToList());
        var reversed = DuplicateReview.Plan(MapOf([.. Enumerable.Reverse(copies)]), Enumerable.Reverse(copies).Select(c => Included(c.Path)).ToList());

        var unit = Assert.Single(units);
        Assert.Equal(DuplicateReview.CopyCap, unit.Members.Count);
        Assert.Equal(copies.Take(12).Select(c => c.Id), unit.Members.Select(m => m.Symbol));
        Assert.Equal("C00.Copy and 13 other copies", unit.Key);
        Assert.Equal((unit.Id, Fingerprints.Compute(unit.Members)), (Assert.Single(reversed).Id, Fingerprints.Compute(reversed[0].Members)));
    }

    [Fact]
    public async Task Scan_PlansDuplicateUnits_UnlessDuplicatesAreOff()
    {
        DuplicateGetQuoteAndTheWorkerLoop();

        await ScanAsync(Config.Default with { Duplicates = false });
        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Duplicate && u.Status != UnitStatus.Retired);

        await ScanAsync();
        var unit = Assert.Single(ledger.Units, u => u.Kind == UnitKind.Duplicate);
        Assert.Equal(("duplicate:norm:lookup", UnitStatus.Pending), (unit.Id, unit.Status));
        Assert.Equal([ServiceGetQuote, ExecuteAsync], ledger.Members.Where(m => m.UnitId == unit.Id).Select(m => m.Symbol));
    }

    [Fact]
    public async Task DuplicatePack_ListsEveryCopyWithItsBody_AndAsksForSimplificationFindings()
    {
        DuplicateGetQuoteAndTheWorkerLoop();
        tree.Add(ServicePath, string.Join('\n', Enumerable.Range(1, 30).Select(n => n == 14 ? "        var quote = repository.FindForTenant(tenantId, id);" : $"// service line {n}")) + "\n");
        tree.Add(WorkerPath, string.Join('\n', Enumerable.Range(1, 20).Select(n => n == 11 ? "            var open = quotes.ListQuotes(1);" : $"// worker line {n}")) + "\n");
        await ScanAsync();

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Duplicate).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("- kind: duplicate\n", pack);
        Assert.Contains("- lenses: duplicate\n", pack);
        Assert.Contains("category \"simplification\" and severity \"low\"", pack);
        Assert.Contains("tests, generated DTOs", pack);
        Assert.Contains($"- {ServicePath}:13-18 QuoteService.GetQuote", pack);
        Assert.Contains($"- {WorkerPath}:7-15 ReminderWorker.ExecuteAsync", pack);
        Assert.Contains("14 |         var quote = repository.FindForTenant(tenantId, id);", pack);
        Assert.Contains("11 |             var open = quotes.ListQuotes(1);", pack);
        Assert.Contains($"### {ServicePath} :: {ServiceGetQuote} (csharp)\n\nlines 13-18, copy 1 of 2", pack);
        Assert.DoesNotContain("capped", pack);
    }

    [Fact]
    public async Task DuplicatePack_StatesTheCap_WhenTheGroupHasMoreThanTwelveCopies()
    {
        var copies = Enumerable.Range(0, 13).Select(i => At($"M:C{i:00}.Copy", $"src/C{i:00}.cs", 1, 6, "wide")).ToList();
        csharp.Map = MapOf([.. copies]);
        typescript.Map = MapOf();
        foreach (var copy in copies) tree.Add(copy.Path, "a\nb\nc\nd\ne\nf\n");
        await ScanAsync();

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Duplicate).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("13 copies in all; capped at the first 12 by path, which are shown and are this unit's members.", pack);
        Assert.DoesNotContain("src/C12.cs", pack);
    }

    [Fact]
    public async Task RunWithAnAgent_RecordsTheDuplicateCall_AndItsSimplificationFindings_UnderTheDuplicateKind()
    {
        DuplicateGetQuoteAndTheWorkerLoop();
        await ScanAsync();
        var events = new RecordingEvents();
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(AnalysisResponseJson.Serialize(new AnalysisResponse("Two copies of one lookup.",
            [
                new Finding(ServicePath, 13, 18, Severity.Low, "simplification", "Same loop as ReminderWorker.ExecuteAsync.", "Both bodies match.", 0.8, "duplicate"),
                new Finding(WorkerPath, 7, 15, Severity.Low, "simplification", "Same loop as QuoteService.GetQuote.", "Both bodies match.", 0.8, "duplicate"),
            ]))))
        { Usage = new AgentUsage(10, 2, 0, 0, "fake", null) };

        var result = await new Run(ledger, tree, clock, Config.Default with { BatchUnits = 1, Verify = false }, adapter, new Progress<RunProgress>(), null, events)
            .RunAsync(new RunOptions(1, 1, false, UnitKind.Duplicate), CancellationToken.None);

        Assert.Equal(1, result.Completed);
        Assert.Equal(UnitKind.Duplicate, Assert.Single(ledger.Calls).Kind);
        Assert.Equal("duplicate", events.OfType("unit_finished").Single().Fields["kind"]);
        Assert.Equal(2, (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Count(f => Config.IsSimplification(f.Finding)));
    }
}
