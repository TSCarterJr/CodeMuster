using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class ImpactTests
{
    private const string Root = "/repos/mixed-repo";
    private const string First = "1111111111111111111111111111111111111111";
    private const string Second = "2222222222222222222222222222222222222222";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new() { HeadCommit = First };
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, CSharp());
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, TypeScript() with { HttpCalls = HttpCalls() });

    public ImpactTests() => AddTo(tree);

    private Task<ScanResult> ScanAsync(Config? config = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Config.Default, [csharp, typescript], Root).RunAsync(CancellationToken.None);

    private IReadOnlyList<Unit> Live(UnitKind kind) => ledger.Units.Where(u => u.Kind == kind && u.Status != UnitStatus.Retired).ToList();

    private IEnumerable<string> MembersOf(string unitId) =>
        ledger.Members.Where(m => m.UnitId == unitId).Select(m => $"{m.Distance} {m.Path} {m.Symbol}").Order(StringComparer.Ordinal);

    private async Task ScanChangingAsync(string symbolId, Func<Symbol, Symbol> change)
    {
        await ScanAsync();
        tree.HeadCommit = Second;
        csharp.Map = csharp.Map with { Symbols = csharp.Map.Symbols.Select(s => s.Id == symbolId ? change(s) : s).ToList() };
        typescript.Map = typescript.Map with { Symbols = typescript.Map.Symbols.Select(s => s.Id == symbolId ? change(s) : s).ToList() };
        Edit(csharp.Map.Symbols.Concat(typescript.Map.Symbols).First(s => s.Id == symbolId).Path);
        await ScanAsync();
    }

    // The file behind a changed symbol changes too, or the scan would rightly reuse the stored map (D66); appending keeps its line numbers.
    private void Edit(string path) => tree.Add(path, tree.Contents[path] + "// edited\n");

    [Fact]
    public async Task FirstScan_PlansNoImpactUnits()
    {
        await ScanAsync();

        Assert.Empty(Live(UnitKind.Impact));
    }

    [Fact]
    public async Task ChangedBody_PlansOneImpactUnit_WithThePreviousVersion_CallersUpToTheDepthCap_AndCallees()
    {
        await ScanChangingAsync(ToSummary, s => s with { BodyHash = "body:changed", Range = new LineRange(24, 29) });

        var unit = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal((UnitIds.Impact(ToSummary), UnitStatus.Pending, "QuoteService.ToSummary"), (unit.Id, unit.Status, unit.Key));
        Assert.Equal(
            new[]
            {
                $"-1 {ServicePath} {ToSummary}@{First}",
                $"0 {ServicePath} {ToSummary}",
                $"1 {MoneyPath} {MoneyFormat}",
                $"1 {ServicePath} {ServiceGetQuote}",
                $"1 {ServicePath} {ServiceListQuotes}",
                $"2 {ControllerPath} {ControllerGetQuote}",
                $"2 {ControllerPath} {ControllerListQuotes}",
                $"2 {WorkerPath} {ExecuteAsync}",
                $"3 {ApiPath} {FetchQuotes}",
                $"4 {UseQuotesPath} {UseQuotes}",
            }.Order(StringComparer.Ordinal),
            MembersOf(unit.Id));
        var previous = ledger.Members.Single(m => m.UnitId == unit.Id && m.Distance < 0);
        Assert.Equal(new LineRange(24, 27), previous.Range);
        Assert.Equal(unit.Fingerprint, Fingerprints.Compute(ledger.Members.Where(m => m.UnitId == unit.Id)));
    }

    [Fact]
    public async Task ChangedSignature_IsAChange_NewAndDeletedSymbolsAreNot()
    {
        await ScanAsync();
        tree.HeadCommit = Second;
        csharp.Map = CSharp() with
        {
            Symbols =
            [
                .. CSharp().Symbols.Where(s => s.Id != ArchiveQuote).Select(s => s.Id == MoneyFormat ? s with { Signature = s.Signature + " // now nullable" } : s),
                new Symbol("M:MixedRepo.Api.Shared.Money.Round(System.Decimal)", MoneyPath, new LineRange(12, 15), "method", "public static class Money\npublic static decimal Round(decimal amount)", "body:round"),
            ],
        };

        Edit(MoneyPath);
        Edit(ServicePath);
        await ScanAsync();

        Assert.Equal([UnitIds.Impact(MoneyFormat)], Live(UnitKind.Impact).Select(u => u.Id));
    }

    [Fact]
    public async Task UnchangedRescan_KeepsAPendingImpactUnit_UntilItIsAnalyzed()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        var planned = Assert.Single(Live(UnitKind.Impact));

        Edit(MoneyPath);
        await ScanAsync();

        var kept = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(planned.Id, kept.Id);
        Assert.Equal(UnitStatus.Pending, kept.Status);
    }

    [Fact]
    public async Task APendingImpactUnit_ChangedAgain_KeepsItsFirstBaseline()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });

        const string third = "3333333333333333333333333333333333333333";
        tree.HeadCommit = third;
        csharp.Map = csharp.Map with { Symbols = csharp.Map.Symbols.Select(s => s.Id == ListForTenant ? s with { BodyHash = "body:again" } : s).ToList() };
        Edit(RepositoryPath);
        await ScanAsync();

        var again = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(UnitStatus.Pending, again.Status);
        Assert.Contains(ledger.Members, m => m.UnitId == again.Id && m.Symbol == $"{ListForTenant}@{First}");
        Assert.DoesNotContain(ledger.Members, m => m.UnitId == again.Id && m.Symbol == $"{ListForTenant}@{Second}");
    }

    [Fact]
    public async Task UnchangedRescan_RetiresADoneImpactUnit_AndAChangeAgainPlansItAgainstTheNewBase()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        var planned = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(DoneOutcome.Recorded, (await new Done(ledger, clock, Config.Default).RunAsync(planned.Id, planned.Fingerprint, """{ "summary": "Callers still fine.", "findings": [] }""", CancellationToken.None)).Outcome);

        Edit(MoneyPath);
        await ScanAsync();

        Assert.Empty(Live(UnitKind.Impact));
        Assert.Equal(UnitStatus.Retired, ledger.Units.Single(u => u.Id == planned.Id).Status);

        const string third = "3333333333333333333333333333333333333333";
        tree.HeadCommit = third;
        csharp.Map = csharp.Map with { Symbols = csharp.Map.Symbols.Select(s => s.Id == ListForTenant ? s with { BodyHash = "body:again" } : s).ToList() };
        Edit(RepositoryPath);
        await ScanAsync();

        var again = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(UnitStatus.Pending, again.Status);
        Assert.Contains(ledger.Members, m => m.UnitId == again.Id && m.Symbol == $"{ListForTenant}@{Second}");
    }

    [Fact]
    public async Task APendingImpactUnit_Retires_WhenItsSymbolIsDeleted()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        var planned = Assert.Single(Live(UnitKind.Impact));

        csharp.Map = csharp.Map with { Symbols = csharp.Map.Symbols.Where(s => s.Id != ListForTenant).ToList(), Edges = csharp.Map.Edges.Where(e => e.From != ListForTenant && e.To != ListForTenant).ToList() };
        Edit(RepositoryPath);
        await ScanAsync();

        Assert.Equal(UnitStatus.Retired, ledger.Units.Single(u => u.Id == planned.Id).Status);
    }

    [Fact]
    public async Task ADoneImpactUnit_GoesStale_WhenItsSymbolChangesAgain()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        var unit = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(DoneOutcome.Recorded, (await new Done(ledger, clock, Config.Default).RunAsync(unit.Id, unit.Fingerprint, """{ "summary": "Callers still fine.", "findings": [] }""", CancellationToken.None)).Outcome);

        csharp.Map = csharp.Map with { Symbols = csharp.Map.Symbols.Select(s => s.Id == ListForTenant ? s with { BodyHash = "body:again" } : s).ToList() };
        Edit(RepositoryPath);
        await ScanAsync();

        Assert.Equal(UnitStatus.Stale, Assert.Single(Live(UnitKind.Impact)).Status);
    }

    [Fact]
    public async Task CallersAreCappedAtFortyNodes_AndTheCutIsCounted()
    {
        const string target = "src/Lib.cs";
        var callee = new Symbol("M:Lib.Target", target, new LineRange(1, 3), "method", "class Lib\nvoid Target()", "body:target");
        var callers = Enumerable.Range(0, 50).Select(i => new Symbol($"M:Lib.Caller{i:00}", target, new LineRange(10 + i, 10 + i), "method", $"class Lib\nvoid Caller{i:00}()", $"body:{i}")).ToList();
        var map = new CodeMap([callee, .. callers], callers.Select(c => new Edge(c.Id, callee.Id, EdgeKind.Call)).ToList(), [], new ResolutionStats(50, 0, []), []);
        var changed = map with { Symbols = [callee with { BodyHash = "body:changed" }, .. callers] };

        var previous = new StoredCodeMap(First, "2026-09-10T00:00:00.0000000Z", map, [Languages.CSharp], []);
        var unit = Assert.Single(ImpactReview.Plan(previous, changed, [new FileRecord(target, Languages.CSharp, "h", 1, "", "", "", null, null, null, null, null, null)]));
        var reach = ImpactReview.Walk(changed, callee.Id);

        Assert.Equal(ImpactReview.CallerNodes, unit.Members.Count(m => m.Distance > 0));
        Assert.Equal((40, 10), (reach.Callers.Count, reach.CallersCut));
    }

    [Fact]
    public async Task ImpactOff_PlansNothing()
    {
        var off = Config.Default with { Impact = false };
        await ScanAsync(off);
        tree.HeadCommit = Second;
        csharp.Map = WithBodyHash(CSharp(), ListForTenant, "body:changed");
        Edit(RepositoryPath);

        await ScanAsync(off);

        Assert.Empty(Live(UnitKind.Impact));
    }

    [Fact]
    public async Task ImpactPack_ShowsThePreviousAndCurrentText_CallerBodies_CalleeSignatures_EntryPointsAndPages_AndTheCaps()
    {
        tree.Add(RepositoryPath, Lines(26, (17, "public IReadOnlyList<Quote> ListForTenant(int tenantId)"), (19, "return _quotes.Where(q => q.TenantId == tenantId).ToList();")));
        tree.Add(ServicePath, Lines(28, (9, "public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)"), (10, "=> repository.ListForTenant(tenantId).Select(ToSummary).ToList();")));
        tree.Committed[(First, RepositoryPath)] = Lines(26, (17, "public IReadOnlyList<Quote> ListForTenant(int tenantId)"), (19, "return _quotes.Where(q => q.Status == \"Open\").ToList();"));
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Impact).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("- kind: impact\n", pack);
        Assert.Contains("does anything upstream or downstream now break or misuse", pack);
        Assert.Contains("committed at 1111111", pack);
        Assert.Contains("19 | return _quotes.Where(q => q.Status == \"Open\").ToList();", pack);
        Assert.Contains("19 | return _quotes.Where(q => q.TenantId == tenantId).ToList();", pack);
        Assert.Contains("10 | => repository.ListForTenant(tenantId).Select(ToSummary).ToList();", pack);
        Assert.Contains("- GET /quotes (http)", pack);
        Assert.Contains("- ReminderWorker (background)", pack);
        Assert.Contains("- /quotes (page), 5 calls up, beyond the depth cap", pack);
        Assert.Contains("callers up to 4 calls up and 40 symbols: 5 shown, 1 more not shown", pack);
        Assert.Contains($"### {RepositoryPath} :: {ListForTenant} (csharp)", pack);
        Assert.DoesNotContain($"{ListForTenant}@{First} (csharp)", pack);
    }

    [Fact]
    public async Task ImpactPack_SaysWhenThePreviousTextIsUnavailable_AndShowsCalleesAsSignatures()
    {
        await ScanChangingAsync(ToSummary, s => s with { BodyHash = "body:changed" });

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Impact).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains($"not available: {ServicePath} is not in commit 1111111", pack);
        Assert.Contains($"### {MoneyPath} :: {MoneyFormat} (csharp)\n\nlines 7-10, callee, shown as its signature\n", pack);
        Assert.Contains("public static string Format(decimal amount)", pack);
    }

    [Fact]
    public async Task RunWithAnAgent_RecordsTheImpactCall_AndItsEvents_UnderTheImpactKind()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        var events = new RecordingEvents();
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult("""{ "summary": "No caller breaks.", "findings": [] }""")) { Usage = new AgentUsage(10, 2, 0, 0, "fake", null) };

        var result = await new Run(ledger, tree, clock, Config.Default with { BatchUnits = 1, Verify = false }, adapter, new Progress<RunProgress>(), null, events)
            .RunAsync(new RunOptions(1, 1, false, UnitKind.Impact), CancellationToken.None);

        Assert.Equal(1, result.Completed);
        Assert.Equal(UnitStatus.Done, ledger.Units.Single(u => u.Id == UnitIds.Impact(ListForTenant)).Status);
        Assert.Equal(UnitKind.Impact, Assert.Single(ledger.Calls).Kind);
        Assert.Equal("impact", events.OfType("unit_finished").Single().Fields["kind"]);
    }

    [Fact]
    public async Task ImpactQuery_WithoutAMap_ExitsTwo()
    {
        var result = await new ImpactQuery(ledger, tree).RunAsync(null, MapFormat.Text, CancellationToken.None);

        Assert.Equal((2, "no code map yet; run codemuster scan"), (result.ExitCode, result.Error));
    }

    [Fact]
    public async Task ImpactQuery_ListsTheImpactUnits_WithTheirBlastRadius()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });

        var result = await new ImpactQuery(ledger, tree).RunAsync(null, MapFormat.Text, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 impact unit from the scan at 2222222 (0 done)", result.Output);
        Assert.Contains($"QuoteRepository.ListForTenant  {RepositoryPath}:17  pending", result.Output);
        Assert.Contains("  callers: QuoteService.ListQuotes, QuotesController.ListQuotes, ReminderWorker.ExecuteAsync, fetchQuotes, useQuotes, QuotesPage", result.Output);
        Assert.Contains("  entry points: GET /quotes (http), ReminderWorker (background)", result.Output);
        Assert.Contains("  pages: /quotes", result.Output);
    }

    [Fact]
    public async Task ImpactQuery_Since_ListsTheStoredSymbolsInFilesChangedSinceTheRef()
    {
        await ScanChangingAsync(ListForTenant, s => s with { BodyHash = "body:changed" });
        tree.Changed["HEAD~1"] = [RepositoryPath, "README.md"];

        var text = await new ImpactQuery(ledger, tree).RunAsync("HEAD~1", MapFormat.Text, CancellationToken.None);
        var json = await new ImpactQuery(ledger, tree).RunAsync("HEAD~1", MapFormat.Json, CancellationToken.None);

        Assert.Contains("2 files changed since HEAD~1; 3 mapped symbols in them", text.Output);
        Assert.Contains($"QuoteRepository.ListForTenant  {RepositoryPath}:17  impact unit pending", text.Output);
        Assert.Contains($"QuoteRepository.FindForTenant  {RepositoryPath}:22", text.Output);
        Assert.Contains("  pages: /quotes", text.Output);
        Assert.Contains("README.md: no mapped symbols", text.Output);
        Assert.Contains($"\"id\": \"{ListForTenant}\"", json.Output);
        Assert.Contains("\"impactUnit\": \"pending\"", json.Output);
    }

    [Fact]
    public async Task ImpactQuery_Since_AnUnknownRef_ExitsTwo()
    {
        await ScanAsync();

        var result = await new ImpactQuery(ledger, tree).RunAsync("nope", MapFormat.Text, CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("unknown revision nope", result.Error);
    }

    private const string QuoteType = "T:MixedRepo.Api.Data.Quote";
    private const string QuoteStatus = "P:MixedRepo.Api.Data.Quote.Status";

    // Quote and its Status property, read by ToSummary and ListForTenant (D72); the fake mapper records no call edge for a read.
    private void AddQuoteDeclarations() => csharp.Map = csharp.Map with
    {
        Declarations =
        [
            new Symbol(QuoteType, QuotePath, new LineRange(3, 3), "record", "public sealed record Quote(int Id, int TenantId, string Customer, decimal Total, string Status)", "decl:quote"),
            new Symbol(QuoteStatus, QuotePath, new LineRange(3, 3), "property", "string Status", "decl:status"),
        ],
        References =
        [
            new Reference(ListForTenant, QuoteStatus, ReferenceKind.Read, RepositoryPath, 19, 40),
            new Reference(ToSummary, QuoteStatus, ReferenceKind.Read, ServicePath, 26, 92),
            new Reference(ToSummary, QuoteType, ReferenceKind.Type, ServicePath, 24, 45),
        ],
    };

    private async Task ScanChangingDeclarationAsync(string declarationId, Func<Symbol, Symbol> change)
    {
        AddQuoteDeclarations();
        await ScanAsync();
        tree.HeadCommit = Second;
        csharp.Map = csharp.Map with { Declarations = csharp.Map.Declarations.Select(d => d.Id == declarationId ? change(d) : d).ToList() };
        Edit(QuotePath);
        await ScanAsync();
    }

    [Fact]
    public async Task ChangedDeclaration_PlansAnImpactUnit_WithItsReaders_AndTheirCallersUpToTheDepthCap()
    {
        await ScanChangingDeclarationAsync(QuoteStatus, d => d with { Signature = "string? Status", BodyHash = "decl:changed" });

        var unit = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal((UnitIds.Impact(QuoteStatus), UnitStatus.Pending, "Quote.Status"), (unit.Id, unit.Status, unit.Key));
        Assert.Equal(
            new[]
            {
                $"-1 {QuotePath} {QuoteStatus}@{First}",
                $"0 {QuotePath} {QuoteStatus}",
                $"1 {RepositoryPath} {ListForTenant}",
                $"1 {ServicePath} {ToSummary}",
                $"2 {ServicePath} {ServiceGetQuote}",
                $"2 {ServicePath} {ServiceListQuotes}",
                $"3 {ControllerPath} {ControllerGetQuote}",
                $"3 {ControllerPath} {ControllerListQuotes}",
                $"3 {WorkerPath} {ExecuteAsync}",
                $"4 {ApiPath} {FetchQuotes}",
            }.Order(StringComparer.Ordinal),
            MembersOf(unit.Id));
    }

    [Fact]
    public async Task AChangedTypeBody_IsNotAnImpactTarget_ButAChangedTypeHeaderIs()
    {
        await ScanChangingDeclarationAsync(QuoteType, d => d with { BodyHash = "decl:members-changed" });

        Assert.Empty(Live(UnitKind.Impact));

        csharp.Map = csharp.Map with { Declarations = csharp.Map.Declarations.Select(d => d.Id == QuoteType ? d with { Signature = d.Signature + " : IQuote" } : d).ToList() };
        Edit(QuotePath);
        await ScanAsync();

        var unit = Assert.Single(Live(UnitKind.Impact));
        Assert.Equal(UnitIds.Impact(QuoteType), unit.Id);
        Assert.Contains($"1 {ServicePath} {ToSummary}", MembersOf(unit.Id));
    }

    [Fact]
    public async Task UnchangedRescan_KeepsAPendingDeclarationImpactUnit()
    {
        await ScanChangingDeclarationAsync(QuoteStatus, d => d with { BodyHash = "decl:changed" });
        var planned = Assert.Single(Live(UnitKind.Impact));

        Edit(MoneyPath);
        await ScanAsync();

        Assert.Equal((planned.Id, UnitStatus.Pending), (Assert.Single(Live(UnitKind.Impact)).Id, Assert.Single(Live(UnitKind.Impact)).Status));
    }

    [Fact]
    public async Task AChangedSymbol_ReachesCodeThatReferencesItWithoutACallEdge()
    {
        csharp.Map = csharp.Map with { References = [new Reference(FindForTenant, MoneyFormat, ReferenceKind.Read, RepositoryPath, 23, 10)] };

        await ScanChangingAsync(MoneyFormat, s => s with { BodyHash = "body:changed" });

        var unit = Assert.Single(Live(UnitKind.Impact));
        Assert.Contains($"1 {RepositoryPath} {FindForTenant}", MembersOf(unit.Id));
        Assert.Contains($"1 {ServicePath} {ToSummary}", MembersOf(unit.Id));
    }

    [Fact]
    public async Task DeclarationImpactPack_ListsEachReferencingSite_WithKindAndLine_AndTheEntryPointsReached()
    {
        tree.Add(QuotePath, Lines(3, (3, "public sealed record Quote(int Id, int TenantId, string Customer, decimal Total, string? Status);")));
        await ScanChangingDeclarationAsync(QuoteStatus, d => d with { Signature = "string? Status", BodyHash = "decl:changed" });

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Impact).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("## References\n", pack);
        Assert.Contains($"- read {RepositoryPath}:19:40 in QuoteRepository.ListForTenant\n", pack);
        Assert.Contains($"- read {ServicePath}:26:92 in QuoteService.ToSummary\n", pack);
        Assert.Contains("- GET /quotes (http), 3 calls up", pack);
        Assert.Contains("- references to the changed symbol, up to 100: 2 shown, 0 more not shown", pack);
        Assert.Contains($"### {QuotePath} :: {QuoteStatus} (csharp)", pack);
    }

    [Fact]
    public async Task ImpactQuery_ListsADeclarationImpactUnit_WithTheCodeThatReadsIt()
    {
        await ScanChangingDeclarationAsync(QuoteStatus, d => d with { BodyHash = "decl:changed" });

        var result = await new ImpactQuery(ledger, tree).RunAsync(null, MapFormat.Text, CancellationToken.None);

        Assert.Contains($"Quote.Status  {QuotePath}:3  pending", result.Output);
        Assert.Contains("  callers: QuoteRepository.ListForTenant, QuoteService.ToSummary", result.Output);
        Assert.Contains("  entry points: GET /quotes (http), GET /quotes/{id} (http), ReminderWorker (background)", result.Output);
    }

    private static string Lines(int count, params (int Line, string Text)[] lines) =>
        string.Join('\n', Enumerable.Range(1, count).Select(n => lines.FirstOrDefault(l => l.Line == n).Text ?? $"// line {n}")) + "\n";
}
