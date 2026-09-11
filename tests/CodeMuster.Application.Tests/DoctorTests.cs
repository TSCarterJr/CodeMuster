using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DoctorTests
{
    private const string RepoRoot = "/repo";

    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, MapWith(2));
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, MapWith(1));

    public DoctorTests()
    {
        tree.Add("MixedRepo.sln", "");
        tree.Add("src/A.cs", "class A {}");
        tree.Add("web/tsconfig.json", "{}");
        tree.Add("web/a.ts", "export const a = 1;");
        csharp.OnMap = () => clock.UtcNow = clock.UtcNow.AddSeconds(9.8);
        typescript.OnMap = () => clock.UtcNow = clock.UtcNow.AddSeconds(1.2);
    }

    private static CodeMap MapWith(int symbols, params string[] diagnostics) => new(
        Enumerable.Range(1, symbols).Select(i => new Symbol($"s{i}", "src/A.cs", new LineRange(i, i), "method", "void M()", "h")).ToList(),
        [], [], new ResolutionStats(0, 0, []), diagnostics);

    private Task<DoctorReport> RunAsync(ISourceTree? source = null) =>
        new Doctor(source ?? tree, [csharp, typescript], clock, RepoRoot).RunAsync(CancellationToken.None);

    [Fact]
    public async Task EveryProbeWorking_ReportsTimeToReady_AndIsReady()
    {
        var report = await RunAsync();

        Assert.True(report.Ready);
        Assert.Equal("git: working\ncsharp: working in 9.8 s, 2 symbol(s)\ntypescript: working in 1.2 s, 1 symbol(s)\nready", report.Render());
        var call = Assert.Single(csharp.Calls);
        Assert.Equal(RepoRoot, call.RepoRoot);
        Assert.Equal(["MixedRepo.sln", "src/A.cs", "web/tsconfig.json", "web/a.ts"], call.Paths);
    }

    [Fact]
    public async Task MapperThatReturnsNoSymbols_IsLoadedButEmpty_WithWhatToCheck()
    {
        csharp.Map = MapWith(0);
        typescript.Map = MapWith(0);

        var report = await RunAsync();

        Assert.False(report.Ready);
        Assert.Equal(
            "git: working\n" +
            "csharp: loaded-but-empty in 9.8 s\n" +
            "  no symbols came back; check that the solution builds with dotnet build\n" +
            "typescript: loaded-but-empty in 1.2 s\n" +
            "  no symbols came back; check that a tracked tsconfig.json includes the TypeScript files\n" +
            "not ready",
            report.Render());
    }

    [Fact]
    public async Task MapperThatThrowsOrReportsDiagnostics_IsFailed_WithTheFixCommandPrintedNotRun()
    {
        csharp.Map = MapWith(2, "src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln");
        typescript.Throws = new InvalidOperationException("TypeScript mapping failed: typescript was not found for web/tsconfig.json; run npm ci --prefix web");

        var report = await RunAsync();

        Assert.False(report.Ready);
        Assert.Equal(
            "git: working\n" +
            "csharp: failed in 9.8 s\n" +
            "  src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln\n" +
            "typescript: failed in 1.2 s\n" +
            "  TypeScript mapping failed: typescript was not found for web/tsconfig.json; run npm ci --prefix web\n" +
            "not ready",
            report.Render());
        Assert.Single(csharp.Calls);
        Assert.Single(typescript.Calls);
    }

    [Fact]
    public async Task OnlyLanguagesWithIncludedFiles_AreProbed()
    {
        tree.Files.RemoveAll(f => f.Path.StartsWith("web/", StringComparison.Ordinal));
        tree.Add("web/generated.ts", "export const g = 1;", linguistGenerated: true);

        var report = await RunAsync();

        Assert.Equal("git: working\ncsharp: working in 9.8 s, 2 symbol(s)\nready", report.Render());
        Assert.Empty(typescript.Calls);
    }

    [Fact]
    public async Task ExcludeGlobsFromConfig_KeepFilesOutOfTheProbes()
    {
        var config = Config.Default with { Exclude = ["web/**"] };

        var report = await new Doctor(tree, [csharp, typescript], clock, RepoRoot, config).RunAsync(CancellationToken.None);

        Assert.Equal("git: working\ncsharp: working in 9.8 s, 2 symbol(s)\nready", report.Render());
        Assert.Equal(["MixedRepo.sln", "src/A.cs"], Assert.Single(csharp.Calls).Paths);
        Assert.Empty(typescript.Calls);
    }

    [Fact]
    public async Task Progress_NamesEachStepAsItHappens_WithTheLanguageInFront()
    {
        var progress = new ListProgress();
        csharp.Reports.Add("loading MixedRepo.sln");
        typescript.Reports.Add("loading web/tsconfig.json");

        await new Doctor(tree, [csharp, typescript], clock, RepoRoot, progress: progress).RunAsync(CancellationToken.None);

        Assert.Equal(["git: listed 4 files", "csharp: loading MixedRepo.sln", "typescript: loading web/tsconfig.json"], progress.Messages);
    }

    [Fact]
    public async Task GitThatCannotListFiles_IsFailed_AndNoMapperRuns()
    {
        var report = await RunAsync(new BrokenTree());

        Assert.False(report.Ready);
        Assert.Equal("git: failed\n  git ls-files exited with code 128: not a git repository\nnot ready", report.Render());
        Assert.Empty(csharp.Calls);
    }

    [Fact]
    public void GitFailed_RendersLikeAProbe()
    {
        Assert.Equal("git: failed\n  git was not found\nnot ready", DoctorReport.GitFailed("git was not found").Render());
    }

    private sealed class BrokenTree : ISourceTree
    {
        public Task<string> HeadCommitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SourceFile>>(new InvalidOperationException("git ls-files exited with code 128: not a git repository"));

        public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
