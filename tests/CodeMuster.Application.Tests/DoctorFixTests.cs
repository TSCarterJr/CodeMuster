using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DoctorFixTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "doctor-fix-repo"));
    private const string SdkMessage = "the C# mapper needs the .NET SDK, which was not found; install it from https://dotnet.microsoft.com/download";

    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCommandRunner runner = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, MapWith(2));
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, MapWith(1));

    public DoctorFixTests()
    {
        tree.Add("MixedRepo.sln", "");
        tree.Add("src/A.cs", "class A {}");
        tree.Add("web/package.json", "{}");
        tree.Add("web/tsconfig.json", "{}");
        tree.Add("web/a.ts", "export const a = 1;");
    }

    private static CodeMap MapWith(int symbols, params string[] diagnostics) => new(
        Enumerable.Range(1, symbols).Select(i => new Symbol($"s{i}", "src/A.cs", new LineRange(i, i), "method", "void M()", "h")).ToList(),
        [], [], new ResolutionStats(0, 0, []), diagnostics);

    private Task<DoctorReport> RunAsync(ISourceTree? source = null) =>
        new Doctor(source ?? tree, [csharp, typescript], clock, RepoRoot, commands: runner).RunAsync(CancellationToken.None);

    private static string[] Rendered(DoctorReport report) => report.Fixes.Select(fix => fix.Render()).ToArray();

    [Fact]
    public async Task HealthyRepository_HasNoFixes_AndRunsNoCommand()
    {
        var report = await RunAsync();

        Assert.True(report.Ready);
        Assert.Empty(report.Fixes);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task UnrestoredProjects_OfferOneRestorePerTarget()
    {
        csharp.Map = MapWith(
            2,
            "src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln",
            "src/B/B.csproj is not restored; run dotnet restore MixedRepo.sln",
            "tools/T.csproj is not restored; run dotnet restore tools/T.csproj");

        var report = await RunAsync();

        Assert.Equal(["dotnet restore MixedRepo.sln", "dotnet restore tools/T.csproj"], Rendered(report));
        Assert.All(report.Fixes, fix => Assert.Equal("", fix.Folder));
    }

    [Theory]
    [InlineData("web/package-lock.json", "cd web && npm ci")]
    [InlineData("web/pnpm-lock.yaml", "cd web && pnpm install")]
    [InlineData("web/yarn.lock", "cd web && yarn install")]
    [InlineData(null, "cd web && npm install")]
    public async Task MissingTypeScriptPackage_OffersTheInstallOfTheLockfilesPackageManager(string? lockfile, string expected)
    {
        runner.OnPath.UnionWith(["pnpm", "yarn"]);
        if (lockfile is not null) tree.Add(lockfile, "");
        typescript.Throws = new InvalidOperationException("TypeScript mapping failed: typescript was not found for web/tsconfig.json; run npm ci --prefix web");

        var report = await RunAsync();

        var fix = Assert.Single(report.Fixes);
        Assert.Equal(expected, fix.Render());
        Assert.Equal("web", fix.Folder);
    }

    [Theory]
    [InlineData("web/pnpm-lock.yaml")]
    [InlineData("web/yarn.lock")]
    public async Task LockfileOfAPackageManagerNotOnPath_FallsBackToNpmInstall(string lockfile)
    {
        tree.Add(lockfile, "");
        typescript.Map = MapWith(0, "typescript was not found for web/tsconfig.json; install the dependencies of web with its package manager");

        var report = await RunAsync();

        Assert.Equal(["cd web && npm install"], Rendered(report));
    }

    [Fact]
    public async Task MissingTypeScriptPackage_InstallsInTheNearestFolderWithAPackageJson_OnceForSeveralTsconfigs()
    {
        tree.Files.RemoveAll(f => f.Path.StartsWith("web/", StringComparison.Ordinal));
        tree.Add("package.json", "{}").Add("package-lock.json", "").Add("web/app/tsconfig.json", "{}").Add("web/lib/tsconfig.json", "{}").Add("web/app/a.ts", "");
        typescript.Map = MapWith(
            0,
            "typescript was not found for web/app/tsconfig.json; run npm ci --prefix web/app",
            "typescript was not found for web/lib/tsconfig.json; run npm ci --prefix web/lib");

        var report = await RunAsync();

        Assert.Equal(["npm ci"], Rendered(report));
    }

    [Theory]
    [InlineData("run npm i -D typescript", null, "npm i -D typescript", "")]
    [InlineData("run npm i -D typescript --prefix web", null, "cd web && npm i -D typescript", "web")]
    [InlineData("add typescript as a dev dependency of the repository with its package manager", "pnpm-lock.yaml", "pnpm add -D typescript", "")]
    [InlineData("add typescript as a dev dependency of web with its package manager", "web/yarn.lock", "cd web && yarn add -D typescript", "web")]
    [InlineData("add typescript as a dev dependency of web with its package manager", null, "cd web && npm i -D typescript", "web")]
    public async Task MissingTypeScriptForTheDefaultProgram_AddsTypescriptAsADevDependency_WhereTheMapperSaid(string hint, string? lockfile, string expected, string folder)
    {
        runner.OnPath.UnionWith(["pnpm", "yarn"]);
        tree.Files.RemoveAll(f => f.Path == "web/tsconfig.json");
        tree.Add("package.json", "{}");
        if (lockfile is not null) tree.Add(lockfile, "");
        typescript.Map = MapWith(0, "typescript was not found for the JavaScript and TypeScript files; " + hint);

        var report = await RunAsync();

        var fix = Assert.Single(report.Fixes);
        Assert.Equal(expected, fix.Render());
        Assert.Equal(folder, fix.Folder);
    }

    [Fact]
    public async Task MissingTypeScriptPackage_WithNoPackageJson_OffersNothing()
    {
        tree.Files.RemoveAll(f => f.Path == "web/package.json");
        typescript.Map = MapWith(0, "typescript was not found for web/tsconfig.json; install the dependencies of web with its package manager");

        var report = await RunAsync();

        Assert.Empty(report.Fixes);
    }

    [Fact]
    public async Task RepositoryWithNoCommit_OffersAddAndFirstCommit()
    {
        runner.Results["git rev-parse --verify --quiet HEAD"] = new CommandResult(1, "", "");

        var report = await RunAsync(new BrokenTree());

        Assert.Equal(["git add -A && git commit -m \"Initial commit\""], Rendered(report));
        Assert.Equal((RepoRoot, "git rev-parse --verify --quiet HEAD"), Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task GitThatFailsForAnotherReason_OffersNothing()
    {
        runner.Results["git rev-parse --verify --quiet HEAD"] = new CommandResult(128, "", "fatal: detected dubious ownership");

        var report = await RunAsync(new BrokenTree());

        Assert.Empty(report.Fixes);
    }

    [Fact]
    public void NotARepository_OffersGitInitAndAFirstCommit()
    {
        var report = DoctorReport.NotARepository("/tmp/x is not a git repository or inside one");

        Assert.Equal("git: failed\n  /tmp/x is not a git repository or inside one\nnot ready", report.Render());
        Assert.Equal(["git init && git add -A && git commit -m \"Initial commit\""], Rendered(report));
    }

    [Fact]
    public async Task CSharpMapperThatCannotStartDotnet_SaysToInstallTheSdk_WhenDotnetIsMissing()
    {
        runner.OnPath.Remove("dotnet");
        csharp.Throws = new InvalidOperationException("An error occurred trying to start process 'dotnet.exe' with working directory 'C:\\x'. The system cannot find the file specified.");

        var report = await RunAsync();

        var probe = report.Probes.Single(p => p.Name == Languages.CSharp);
        Assert.Equal(ProbeState.Failed, probe.State);
        Assert.Equal([SdkMessage], probe.Problems);
        Assert.Empty(report.Fixes);
    }

    [Fact]
    public async Task CSharpMapperFailure_SaysToInstallTheSdk_WhenDotnetListsNoSdk()
    {
        runner.Results["dotnet --list-sdks"] = new CommandResult(0, "\n", "");
        csharp.Throws = new InvalidOperationException("No instances of MSBuild could be detected.");

        var report = await RunAsync();

        Assert.Equal([SdkMessage], report.Probes.Single(p => p.Name == Languages.CSharp).Problems);
        Assert.Contains((RepoRoot, "dotnet --list-sdks"), runner.Calls);
    }

    [Fact]
    public async Task CSharpMapperFailure_WithAnSdkInstalled_KeepsItsOwnProblems()
    {
        runner.Results["dotnet --list-sdks"] = new CommandResult(0, "10.0.100 [C:\\Program Files\\dotnet\\sdk]\n", "");
        csharp.Map = MapWith(2, "src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln");

        var report = await RunAsync();

        Assert.Equal(["src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln"], report.Probes.Single(p => p.Name == Languages.CSharp).Problems);
        Assert.Equal(["dotnet restore MixedRepo.sln"], Rendered(report));
    }

    [Fact]
    public async Task WithoutACommandRunner_TheSdkIsNotChecked_AndRestoreFixesAreStillOffered()
    {
        csharp.Map = MapWith(2, "src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln");

        var report = await new Doctor(tree, [csharp, typescript], clock, RepoRoot).RunAsync(CancellationToken.None);

        Assert.Equal(["src/A/A.csproj is not restored; run dotnet restore MixedRepo.sln"], report.Probes.Single(p => p.Name == Languages.CSharp).Problems);
        Assert.Equal(["dotnet restore MixedRepo.sln"], Rendered(report));
    }

    private static readonly DoctorFix Restore = new("", [["dotnet", "restore", "MixedRepo.sln"]]);
    private static readonly DoctorFix Install = new("web", [["npm", "ci"]]);
    private static readonly DoctorFix FirstCommit = new("", [["git", "add", "-A"], ["git", "commit", "-m", "Initial commit"]]);

    private async Task<(int Ran, List<string> Output, List<string> Asked)> ApplyAsync(DoctorFixMode mode, Func<DoctorFix, bool>? answer = null, params DoctorFix[] fixes)
    {
        var output = new ListProgress();
        var asked = new List<string>();
        var ran = await new DoctorFixer(runner, RepoRoot, output).ApplyAsync(fixes, mode, fix =>
        {
            asked.Add(fix.Render());
            return answer?.Invoke(fix) ?? false;
        }, CancellationToken.None);
        return (ran, output.Messages, asked);
    }

    [Fact]
    public async Task PrintOnly_ListsTheCommands_RunsNothing_AndAsksNothing()
    {
        var (ran, output, asked) = await ApplyAsync(DoctorFixMode.PrintOnly, null, Restore, Install);

        Assert.Equal(0, ran);
        Assert.Empty(runner.Calls);
        Assert.Empty(asked);
        Assert.Equal(
            [
                "would run: dotnet restore MixedRepo.sln",
                "would run: cd web && npm ci",
                "nothing was run because this is not an interactive terminal; run codemuster doctor --fix in a terminal, or add --yes",
            ],
            output);
    }

    [Fact]
    public async Task Ask_RunsOnlyTheFixesAccepted_InTheirFolders()
    {
        var (ran, output, asked) = await ApplyAsync(DoctorFixMode.Ask, fix => fix == Install, Restore, Install);

        Assert.Equal(1, ran);
        Assert.Equal(["dotnet restore MixedRepo.sln", "cd web && npm ci"], asked);
        Assert.Equal((Path.Combine(RepoRoot, "web"), "npm ci"), Assert.Single(runner.Calls));
        Assert.Equal(["skipped: dotnet restore MixedRepo.sln", "running: cd web && npm ci", "done: cd web && npm ci"], output);
    }

    [Fact]
    public async Task All_RunsEveryFix_WithoutAsking()
    {
        var (ran, _, asked) = await ApplyAsync(DoctorFixMode.All, null, FirstCommit, Restore);

        Assert.Equal(2, ran);
        Assert.Empty(asked);
        Assert.Equal(
            [(RepoRoot, "git add -A"), (RepoRoot, "git commit -m Initial commit"), (RepoRoot, "dotnet restore MixedRepo.sln")],
            runner.Calls);
    }

    [Fact]
    public async Task FailingCommand_StopsItsFix_SaysWhy_AndTheNextFixStillRuns()
    {
        runner.Results["git add -A"] = new CommandResult(128, "", "fatal: something\nwent wrong\n");

        var (ran, output, _) = await ApplyAsync(DoctorFixMode.All, null, FirstCommit, Restore);

        Assert.Equal(2, ran);
        Assert.Equal([(RepoRoot, "git add -A"), (RepoRoot, "dotnet restore MixedRepo.sln")], runner.Calls);
        Assert.Contains("failed: git add -A exited with code 128: fatal: something\nwent wrong", output);
    }

    private sealed class BrokenTree : ISourceTree
    {
        public Task<string> HeadCommitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SourceFile>>(new InvalidOperationException("git log exited with code 128: fatal: your current branch 'main' does not have any commits yet"));

        public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> ReadFileAtCommitAsync(string commit, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ChangedSinceAsync(string since, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
