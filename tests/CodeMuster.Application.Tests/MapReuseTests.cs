using System.Globalization;
using System.Text;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class MapReuseTests
{
    private const string Root = "/repos/mixed-repo";
    private const string Reused = "reused the code map from 0000000; nothing mapped changed";

    private sealed class Repo
    {
        public FakeLedger Ledger { get; } = new();
        public FakeSourceTree Tree { get; } = new();
        public FakeCodeMapper CSharp { get; } = new(Languages.CSharp, MixedRepo.CSharp() with
        {
            Symbols = [.. MixedRepo.CSharp().Symbols.Select(s => s with { NormalizedHash = s.Id == ControllerGetQuote || s.Id == ExecuteAsync ? "norm:same" : "norm:" + s.Id })],
        });

        public FakeCodeMapper TypeScript { get; } = new(Languages.TypeScript, MixedRepo.TypeScript() with
        {
            HttpCalls = HttpCalls(),
            UiElements = [new UiElement("route", "/quotes", QuotesPagePath, 4, Route: "/quotes"), new UiElement("heading", "Quotes", QuotesPagePath, 8, Route: "/quotes")],
        });

        public List<string> Progress { get; } = [];

        public Repo() => AddTo(Tree);

        public int MapperCalls => CSharp.Calls.Count + TypeScript.Calls.Count;

        public Task<ScanResult> ScanAsync(bool remap = false, Config? config = null) =>
            new Scan(Ledger, Tree, new FakeContentHasher(), new FakeClock(), config ?? Config.Default, [CSharp, TypeScript], Root, new ListProgress(Progress), remap: remap).RunAsync(CancellationToken.None);

        // Everything a scan writes, in order, so a reused map can be compared with a full remap.
        public string Dump()
        {
            var text = new StringBuilder();
            foreach (var unit in Ledger.Units) text.Append(CultureInfo.InvariantCulture, $"unit|{unit}\n");
            foreach (var member in Ledger.Members) text.Append(CultureInfo.InvariantCulture, $"member|{member}\n");
            foreach (var run in Ledger.Runs) text.Append(CultureInfo.InvariantCulture, $"run|{run.HeadCommit}|{run.FilesIncluded}|{run.FilesExcluded}|{run.UnitsTotal}|{run.ResolutionRate}|{string.Join(',', run.TopUnresolvedNames ?? [])}\n");
            var map = Ledger.CodeMap!;
            text.Append(CultureInfo.InvariantCulture, $"map|{map.HeadCommit}|{map.ScannedAt}|{map.InputsDigest}|{string.Join(',', map.MappedLanguages)}|{string.Join(',', map.FailedLanguages)}\n");
            text.Append(CodeMapJson.Serialize(map.Map));
            return text.ToString();
        }
    }

    [Fact]
    public async Task UnchangedRescan_ReusesTheStoredMap_WithoutMapping_AndWritesExactlyWhatAFullRemapWrites()
    {
        var reused = new Repo();
        var remapped = new Repo();
        await reused.ScanAsync();
        await remapped.ScanAsync();
        Assert.Equal(2, reused.MapperCalls);
        Assert.NotNull(reused.Ledger.CodeMap!.InputsDigest);

        await reused.ScanAsync();
        await remapped.ScanAsync(remap: true);

        Assert.Equal(2, reused.MapperCalls);
        Assert.Equal(4, remapped.MapperCalls);
        Assert.Contains(Reused, reused.Progress);
        Assert.DoesNotContain(Reused, remapped.Progress);
        Assert.Contains(reused.Ledger.Units, u => u.Kind == UnitKind.Duplicate);
        Assert.Contains(reused.Ledger.Units, u => u.Kind == UnitKind.Architecture);
        Assert.Contains(reused.Ledger.CodeMap!.Map.Edges, e => e.Kind == EdgeKind.Http);
        Assert.Equal(remapped.Dump(), reused.Dump());
    }

    [Fact]
    public async Task ReusingTwice_StillMatchesAFullRemap()
    {
        var reused = new Repo();
        var remapped = new Repo();
        await reused.ScanAsync();
        await remapped.ScanAsync();

        await reused.ScanAsync();
        await reused.ScanAsync();
        await remapped.ScanAsync(remap: true);
        await remapped.ScanAsync(remap: true);

        Assert.Equal(2, reused.MapperCalls);
        Assert.Equal(remapped.Dump(), reused.Dump());
    }

    [Theory]
    [InlineData(ServicePath)]
    [InlineData(ApiPath)]
    [InlineData("web/tsconfig.json")]
    [InlineData("web/package.json")]
    [InlineData("Directory.Build.props")]
    [InlineData("MixedRepo.sln")]
    [InlineData("src/MixedRepo.Api/MixedRepo.Api.csproj")]
    public async Task AChangedFileOrMappingInput_MapsEverythingAgain(string path)
    {
        var repo = new Repo();
        await repo.ScanAsync();
        repo.Tree.Add(path, "changed content of " + path);

        await repo.ScanAsync();

        Assert.Equal(4, repo.MapperCalls);
        Assert.DoesNotContain(Reused, repo.Progress);
    }

    [Fact]
    public async Task AnAddedOrExcludedFile_MapsEverythingAgain()
    {
        var added = new Repo();
        await added.ScanAsync();
        added.Tree.Add("src/MixedRepo.Api/Shared/Extra.cs", "class Extra {}");
        await added.ScanAsync();

        var excluded = new Repo();
        await excluded.ScanAsync();
        await excluded.ScanAsync(config: Config.Default with { Exclude = ["web/lib/**"] });

        Assert.Equal((4, 4), (added.MapperCalls, excluded.MapperCalls));
    }

    [Fact]
    public async Task APartialMap_IsNeverReused()
    {
        var failed = new Repo();
        failed.CSharp.Throws = new InvalidOperationException("MSBuild could not load MixedRepo.sln");
        await failed.ScanAsync();
        failed.CSharp.Throws = null;
        await failed.ScanAsync();

        var diagnosed = new Repo();
        diagnosed.CSharp.Map = diagnosed.CSharp.Map with { Diagnostics = ["src/MixedRepo.Api/MixedRepo.Api.csproj is not restored; run dotnet restore MixedRepo.sln"] };
        await diagnosed.ScanAsync();
        await diagnosed.ScanAsync();

        Assert.Null(diagnosed.Ledger.CodeMap!.InputsDigest);
        Assert.Equal((4, 4), (failed.MapperCalls, diagnosed.MapperCalls));
    }

    [Fact]
    public async Task ALanguageTheMapperSkipped_IsNeverReused()
    {
        var repo = new Repo();
        repo.TypeScript.Map = new CodeMap([], [], [], new ResolutionStats(0, 0, []), []) { SkippedLanguages = [new SkippedLanguage(Languages.TypeScript, "typescript: not mapped")] };
        await repo.ScanAsync();

        await repo.ScanAsync();

        Assert.Equal(4, repo.MapperCalls);
    }

    [Fact]
    public async Task APendingImpactUnit_IsRetiredByAReusingScan_AsByAFullOne()
    {
        var reused = new Repo();
        var remapped = new Repo();
        foreach (var repo in new[] { reused, remapped })
        {
            await repo.ScanAsync();
            repo.CSharp.Map = WithBodyHash(repo.CSharp.Map, ListForTenant, "body:changed");
            repo.Tree.Add(RepositoryPath, "changed repository");
            await repo.ScanAsync();
            Assert.Single(repo.Ledger.Units, u => u.Kind == UnitKind.Impact && u.Status == UnitStatus.Pending);
        }

        await reused.ScanAsync();
        await remapped.ScanAsync(remap: true);

        Assert.Equal(4, reused.MapperCalls);
        Assert.Equal(remapped.Dump(), reused.Dump());
        Assert.Equal(UnitStatus.Retired, reused.Ledger.Units.Single(u => u.Kind == UnitKind.Impact).Status);
    }
}
