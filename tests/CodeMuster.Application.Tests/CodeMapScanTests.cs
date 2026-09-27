using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class CodeMapScanTests
{
    private const string Root = "/repos/mixed-repo";

    // SHA-256 of Digest() after scanning the MixedRepo fake, taken on the build before the code map was stored (68fca57).
    private const string UnitsBeforeTheMapWasStored = "3ad3f7da46aac86625f3fde409187a216e87627089ddfb9fb97e1f86886cb50f";

    // The pinned digest predates the api unit (D69), which the MixedRepo fake's endpoints would add; the units it pins are unchanged.
    private static readonly Config Pinned = Config.Default with { ArchitectureReview = false };

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, CSharp());
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, TypeScript());

    public CodeMapScanTests() => AddTo(tree);

    private Task<ScanResult> ScanAsync(bool fileMode = false) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, Pinned, fileMode ? [] : [csharp, typescript], Root).RunAsync(CancellationToken.None);

    private string Digest()
    {
        var text = new StringBuilder();
        foreach (var unit in ledger.Units)
        {
            text.Append($"unit|{unit.Id}|{unit.Kind}|{unit.Key}|{unit.Fingerprint}|{unit.Status}|{unit.Fidelity}|{unit.LensHash}|{unit.Summary}|{unit.SummaryHash}\n");
        }

        foreach (var member in ledger.Members)
        {
            text.Append($"member|{member.UnitId}|{member.Path}|{member.Symbol}|{member.MemberHash}|{member.Distance}|{member.Range?.StartLine}|{member.Range?.EndLine}|{member.Signature}\n");
        }

        foreach (var run in ledger.Runs)
        {
            text.Append($"run|{run.HeadCommit}|{run.FilesIncluded}|{run.FilesExcluded}|{run.UnitsTotal}|{run.ResolutionRate?.ToString("R", CultureInfo.InvariantCulture)}|{string.Join(',', run.TopUnresolvedNames ?? [])}\n");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString().ReplaceLineEndings("\n"))));
    }

    [Fact]
    public async Task Scan_PlansTheSameUnitsMembersAndRun_AsBeforeTheMapWasStored()
    {
        var result = await ScanAsync();

        Assert.Equal((14, 10, 11, 0, 11), (result.FilesIncluded, result.FilesExcluded, result.UnitsCreated, result.UnitsStale, result.UnitsTotal));
        Assert.Equal(UnitsBeforeTheMapWasStored, Digest());
    }

    [Fact]
    public async Task Scan_StoresExactlyTheMappersMap_AtTheScannedCommit_ReadingItOnlyToFindChangedSymbols()
    {
        csharp.Map = CSharp() with { Resolution = new ResolutionStats(17, 3, ["GetService"]) };
        typescript.Map = TypeScript() with { Resolution = new ResolutionStats(11, 1, ["fetch"]), Diagnostics = ["typescript: no jsx factory"] };

        await ScanAsync();

        var stored = Assert.IsType<StoredCodeMap>(ledger.CodeMap);
        Assert.Equal(1, ledger.CodeMapWrites);
        Assert.Equal(1, ledger.CodeMapReads);
        Assert.Equal((ledger.Runs[^1].HeadCommit, ledger.Runs[^1].StartedAt), (stored.HeadCommit, stored.ScannedAt));
        Assert.Equal(csharp.Map.Symbols.Concat(typescript.Map.Symbols), stored.Map.Symbols);
        Assert.Equal(csharp.Map.Edges.Concat(typescript.Map.Edges), stored.Map.Edges);
        Assert.Equal(csharp.Map.EntryPoints.Concat(typescript.Map.EntryPoints), stored.Map.EntryPoints);
        Assert.Equal((28, 4), (stored.Map.Resolution.Resolved, stored.Map.Resolution.Unresolved));
        Assert.Equal(["GetService", "fetch"], stored.Map.Resolution.TopUnresolvedNames);
        Assert.Equal(["typescript: no jsx factory"], stored.Map.Diagnostics);
        Assert.Equal([Languages.CSharp, Languages.TypeScript], stored.MappedLanguages);
        Assert.Empty(stored.FailedLanguages);
    }

    [Fact]
    public async Task Scan_StoresUiToApiLinks_WithoutChangingUnitsOrItsResult()
    {
        typescript.Map = TypeScript() with { HttpCalls = HttpCalls() };
        var progress = new List<string>();

        var result = await new Scan(ledger, tree, new FakeContentHasher(), clock, Pinned, [csharp, typescript], Root, new ListProgress(progress)).RunAsync(CancellationToken.None);

        Assert.Equal(UnitsBeforeTheMapWasStored, Digest());
        Assert.Empty(result.SliceMode!.Diagnostics);
        var stored = ledger.CodeMap!;
        Assert.Equal([.. CSharp().Edges, .. TypeScript().Edges, new Edge(FetchQuotes, ControllerListQuotes, EdgeKind.Http)], stored.Map.Edges);
        Assert.Equal(
            [
                "http: web/lib/api.ts:14 GET /customers matches no endpoint",
                "http: GET /quotes/{id} is not called from the mapped UI",
            ],
            stored.Map.Diagnostics);
        Assert.False(stored.IsPartial);
        Assert.Single(progress, message => message.StartsWith("linked ", StringComparison.Ordinal));
        Assert.Contains("linked 1 UI call to an endpoint; 1 call and 1 endpoint unmatched", progress);
        Assert.True(progress.IndexOf("linked 1 UI call to an endpoint; 1 call and 1 endpoint unmatched") > progress.IndexOf("planning units"));
    }

    [Fact]
    public async Task Scan_WithoutHttpCalls_ReportsNoLinksAndNoUncalledEndpoints()
    {
        var progress = new List<string>();

        await new Scan(ledger, tree, new FakeContentHasher(), clock, Pinned, [csharp, typescript], Root, new ListProgress(progress)).RunAsync(CancellationToken.None);

        Assert.DoesNotContain(ledger.CodeMap!.Map.Edges, edge => edge.Kind == EdgeKind.Http);
        Assert.Empty(ledger.CodeMap.Map.Diagnostics);
        Assert.DoesNotContain(progress, message => message.StartsWith("linked ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rescan_ReplacesTheStoredMap_AndLeavesUnitsAsBefore()
    {
        await ScanAsync();
        var units = ledger.Units.ToList();
        csharp.Map = CSharp() with { Symbols = CSharp().Symbols.Take(3).ToList(), Edges = [], EntryPoints = [] };
        tree.Add(ControllerPath, "content of an edited controller");

        await ScanAsync();

        Assert.Equal(2, ledger.CodeMapWrites);
        Assert.Equal(csharp.Map.Symbols.Concat(typescript.Map.Symbols), ledger.CodeMap!.Map.Symbols);
        Assert.Equal(typescript.Map.EntryPoints, ledger.CodeMap.Map.EntryPoints);
    }

    [Fact]
    public async Task AFailedMapper_StoresWhatWasMapped_WithTheFailureAlongside()
    {
        csharp.Throws = new InvalidOperationException("MSBuild could not load MixedRepo.Api.csproj");

        await ScanAsync();

        var stored = ledger.CodeMap!;
        Assert.True(stored.IsPartial);
        Assert.Equal(typescript.Map.Symbols, stored.Map.Symbols);
        Assert.Equal([Languages.TypeScript], stored.MappedLanguages);
        Assert.Equal([Languages.CSharp], stored.FailedLanguages);
        Assert.Equal(["csharp mapper failed: MSBuild could not load MixedRepo.Api.csproj"], stored.Map.Diagnostics);
    }

    [Fact]
    public async Task FileModeScan_StoresNoMap_AndKeepsTheLastOne()
    {
        await ScanAsync(fileMode: true);
        Assert.Null(ledger.CodeMap);

        await ScanAsync();
        var stored = ledger.CodeMap;
        await ScanAsync(fileMode: true);

        Assert.Same(stored, ledger.CodeMap);
        Assert.Equal(1, ledger.CodeMapWrites);
    }
}
