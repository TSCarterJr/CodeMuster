using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class SliceScanTests
{
    private const string Root = "/repos/mixed-repo";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, CSharp());
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, TypeScript());

    public SliceScanTests() => AddTo(tree);

    private Task<ScanResult> ScanAsync(Config? config = null, bool fileMode = false) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Config.Default, [csharp, typescript], Root, fileMode).RunAsync(CancellationToken.None);

    private Unit UnitById(string id) => ledger.Units.Single(u => u.Id == id);

    private async Task MarkDoneAsync(string unitId, Config? config = null)
    {
        var unit = UnitById(unitId);
        var result = await new Done(ledger, clock, config ?? Config.Default)
            .RunAsync(unit.Id, unit.Fingerprint, """{ "summary": "Audited.", "findings": [] }""", CancellationToken.None);
        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
    }

    private async Task ScanAndMarkEveryUnitDoneAsync()
    {
        await ScanAsync();
        foreach (var unit in ledger.Units.ToList())
        {
            await MarkDoneAsync(unit.Id);
        }
    }

    private IEnumerable<string> IdsWith(UnitStatus status) =>
        ledger.Units.Where(u => u.Status == status).Select(u => u.Id).Order(StringComparer.Ordinal);

    [Fact]
    public async Task SliceMode_PlansSlicesOrphansAndFileUnits_AndRecordsWhatTheMappersResolved()
    {
        csharp.Map = CSharp() with { Resolution = new ResolutionStats(17, 3, ["GetService"]) };
        typescript.Map = TypeScript() with { Resolution = new ResolutionStats(11, 1, ["fetch"]), Diagnostics = ["typescript: no jsx factory"] };

        var result = await ScanAsync();

        Assert.Equal((20, 4, 17, 0, 17), (result.FilesIncluded, result.FilesExcluded, result.UnitsCreated, result.UnitsStale, result.UnitsTotal));
        var slice = Assert.IsType<SliceModeResult>(result.SliceMode);
        Assert.Equal((5, 2, 10, 0.875), (slice.Slices, slice.Orphans, slice.Files, slice.ResolutionRate));
        Assert.Equal(new[] { "typescript: no jsx factory" }, slice.Diagnostics);

        var run = Assert.Single(ledger.Runs);
        Assert.Equal((20, 4, 17, 0.875), (run.FilesIncluded, run.FilesExcluded, run.UnitsTotal, run.ResolutionRate));
        Assert.Equal(new[] { "GetService", "fetch" }, run.TopUnresolvedNames);

        var included = ledger.Files.Values.Where(f => f.ExcludedReason is null).Select(f => f.Path).ToList();
        Assert.All(new[] { csharp, typescript }, mapper =>
        {
            var call = Assert.Single(mapper.Calls);
            Assert.Equal(Root, call.RepoRoot);
            Assert.Equal(included, call.Paths);
        });

        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Pending, u.Status));
        var getQuotes = UnitById(UnitIds.Slice(ControllerListQuotes));
        Assert.Equal((UnitKind.Slice, "GET /quotes", Fidelity.Full), (getQuotes.Kind, getQuotes.Key, getQuotes.Fidelity));
        var members = ledger.Members.Where(m => m.UnitId == getQuotes.Id).ToList();
        Assert.Equal(5, members.Count);
        Assert.Equal(Fingerprints.Compute(members), getQuotes.Fingerprint);
        var program = Assert.Single(ledger.Members, m => m.UnitId == UnitIds.File(ProgramPath));
        Assert.Equal(ledger.Files[ProgramPath].ContentHash, program.MemberHash);
    }

    [Fact]
    public async Task SliceMode_RescanWithNothingChanged_KeepsEveryDoneUnitDone_EvenWithALensOnOneMemberFile()
    {
        var config = new Config([Config.Default.Lenses[0], new Lens("money", "Check rounding.", ["src/MixedRepo.Api/Shared/**"], [])]);
        await ScanAsync(config);
        foreach (var unit in ledger.Units.ToList())
        {
            await MarkDoneAsync(unit.Id, config);
        }

        var result = await ScanAsync(config);

        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Done, u.Status));
        Assert.Equal((0, 0, 17), (result.UnitsCreated, result.UnitsStale, result.UnitsTotal));
    }

    [Fact]
    public async Task SliceMode_LensEdit_MarksOnlyTheUnitsWithAMemberItCovers_Stale()
    {
        var before = new Config([Config.Default.Lenses[0], new Lens("money", "Check rounding.", ["src/MixedRepo.Api/Shared/**"], [])]);
        await ScanAsync(before);
        foreach (var unit in ledger.Units.ToList())
        {
            await MarkDoneAsync(unit.Id, before);
        }

        await ScanAsync(new Config([before.Lenses[0], before.Lenses[1] with { Instructions = "Check rounding and currency." }]));

        Assert.Equal(new[] { ControllerGetQuote, ControllerListQuotes, ExecuteAsync }.Select(UnitIds.Slice).Order(StringComparer.Ordinal), IdsWith(UnitStatus.Stale));
    }

    [Fact]
    public async Task EditingTheSharedHelper_MarksEverySliceThatContainsIt_Stale_AndNothingElse()
    {
        await ScanAndMarkEveryUnitDoneAsync();
        csharp.Map = WithBodyHash(CSharp(), MoneyFormat, "body:edited");
        tree.Add(MoneyPath, "content of an edited Money.cs");

        var result = await ScanAsync();

        Assert.Equal(new[] { ControllerGetQuote, ControllerListQuotes, ExecuteAsync }.Select(UnitIds.Slice).Order(StringComparer.Ordinal), IdsWith(UnitStatus.Stale));
        Assert.Equal((3, 0), (result.UnitsStale, result.UnitsCreated));
        Assert.Equal(14, IdsWith(UnitStatus.Done).Count());
    }

    [Fact]
    public async Task EditingTheDeadMethod_MarksNoSliceStale_OnlyItsOrphan()
    {
        await ScanAndMarkEveryUnitDoneAsync();
        csharp.Map = WithBodyHash(CSharp(), ArchiveQuote, "body:edited");
        tree.Add(ServicePath, "content of an edited QuoteService.cs");

        var result = await ScanAsync();

        Assert.Equal(new[] { UnitIds.Orphan(ServicePath) }, IdsWith(UnitStatus.Stale));
        Assert.Equal((1, 0), (result.UnitsStale, result.UnitsCreated));
    }

    [Fact]
    public async Task ThrowingMapper_IsReported_AndItsLanguagesFilesStayLowFidelityFileUnits()
    {
        csharp.Throws = new InvalidOperationException("MSBuild could not load MixedRepo.sln");

        var result = await ScanAsync();

        var slice = Assert.IsType<SliceModeResult>(result.SliceMode);
        Assert.Equal(new[] { "csharp mapper failed: MSBuild could not load MixedRepo.sln" }, slice.Diagnostics);
        Assert.Equal((2, 0, 15, 1.0), (slice.Slices, slice.Orphans, slice.Files, slice.ResolutionRate));
        var low = ledger.Units.Where(u => u.Fidelity == Fidelity.Low).ToList();
        Assert.Equal(8, low.Count);
        Assert.All(low, u => Assert.Equal((UnitKind.File, Languages.CSharp), (u.Kind, Languages.FromPath(u.Key))));
    }

    [Fact]
    public async Task ForcedFileMode_NeverRunsTheMappers_AndPlansOneFileUnitPerIncludedFile()
    {
        var result = await ScanAsync(fileMode: true);

        Assert.Empty(csharp.Calls);
        Assert.Empty(typescript.Calls);
        Assert.Null(result.SliceMode);
        Assert.Equal((20, 4, 20, 20), (result.FilesIncluded, result.FilesExcluded, result.UnitsCreated, result.UnitsTotal));
        Assert.All(ledger.Units, u => Assert.Equal((UnitKind.File, Fidelity.Full), (u.Kind, u.Fidelity)));
        var run = Assert.Single(ledger.Runs);
        Assert.Null(run.ResolutionRate);
        Assert.Null(run.TopUnresolvedNames);
    }

    [Fact]
    public async Task SwitchingAFileModeLedgerToSliceMode_RetiresFileUnitsOfMappedCode_AndKeepsFileUnitsThatStayFiles()
    {
        await ScanAsync(fileMode: true);
        await MarkDoneAsync(UnitIds.File(ProgramPath));
        await MarkDoneAsync(UnitIds.File(ServicePath));

        var result = await ScanAsync();

        var mapped = new[] { ControllerPath, RepositoryPath, ServicePath, MoneyPath, WorkerPath, CustomersPagePath, QuotesPagePath, QuoteTablePath, UseQuotesPath, ApiPath };
        Assert.Equal(mapped.Select(UnitIds.File).Order(StringComparer.Ordinal), IdsWith(UnitStatus.Retired));
        Assert.Equal(new[] { UnitIds.File(ProgramPath) }, IdsWith(UnitStatus.Done));
        Assert.Equal(UnitStatus.Pending, UnitById(UnitIds.Orphan(ServicePath)).Status);
        Assert.Single(ledger.Members, m => m.UnitId == UnitIds.File(ServicePath));
        Assert.Equal((7, 17), (result.UnitsCreated, result.UnitsTotal));
        Assert.Equal(17, ledger.Runs[^1].UnitsTotal);
    }

    [Fact]
    public async Task SwitchingBackToFileMode_RetiresSlicesAndOrphans_AndRevivesTheRetiredFileUnitsAsPending()
    {
        await ScanAsync(fileMode: true);
        await MarkDoneAsync(UnitIds.File(ProgramPath));
        await ScanAsync();
        await MarkDoneAsync(UnitIds.Slice(ControllerListQuotes));

        var result = await ScanAsync(fileMode: true);

        var sliced = ledger.Units.Where(u => u.Kind is UnitKind.Slice or UnitKind.Orphan).ToList();
        Assert.Equal(7, sliced.Count);
        Assert.All(sliced, u => Assert.Equal(UnitStatus.Retired, u.Status));
        Assert.Equal(new[] { UnitIds.File(ProgramPath) }, IdsWith(UnitStatus.Done));
        Assert.Equal(UnitStatus.Pending, UnitById(UnitIds.File(ServicePath)).Status);
        Assert.Equal((10, 20), (result.UnitsCreated, result.UnitsTotal));
        Assert.Null(result.SliceMode);
        Assert.Null(ledger.Runs[^1].ResolutionRate);
    }
}
