using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class FixUxFreshnessTests
{
    private const string At = "2026-09-15T00:00:00.0000000Z";
    private const string Path = "web/Invoice.tsx";
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly Config config = Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true, BaseUrl = "http://localhost:3000" } };

    [Theory]
    [InlineData("disabled")]
    [InlineData("excluded")]
    [InlineData("url")]
    public async Task ConfigChangedAfterScanCannotPlanAnOldBrowserRepair(string change)
    {
        await SeedAsync();
        var settings = change switch
        {
            "disabled" => config.UserExperience with { Enabled = false },
            "excluded" => config.UserExperience with { Exclude = [Path] },
            _ => config.UserExperience with { BaseUrl = "http://localhost:4000" },
        };

        var plan = await new Fix(ledger, config: config with { UserExperience = settings }).PlanAsync(CancellationToken.None);

        Assert.Equal(new FixPlan(0, 0, 0), plan);
    }

    [Fact]
    public async Task CurrentBrowserSettingsCanPlanConfirmedMeasuredRepair()
    {
        await SeedAsync();

        Assert.Equal(new FixPlan(1, 1, 1), await new Fix(ledger, config: config).PlanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ChangedSourceWithoutAScanIsRejectedBeforeStashingOrEditing()
    {
        await SeedAsync();
        tree.Add(Path, "export function Invoice() { return <p>Changed invoice</p>; }");
        var workspace = new FakeWorkspace { Clean = false };
        var editor = new NeverFixer();
        var error = await Assert.ThrowsAsync<JsonException>(() => new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor, new FakeContentHasher())
            .RunAsync(Adapter(), new FixOptions(Stash: true), null, CancellationToken.None));

        Assert.Contains("source", error.Message);
        Assert.Contains("scan", error.Message);
        Assert.Equal(0, workspace.Stashes);
        Assert.False(workspace.Clean);
        Assert.Equal(0, editor.Calls);
        Assert.Empty(workspace.Commits);
    }

    [Fact]
    public async Task MissingCurrentSourceCheckerCannotStartBrowserRepairs()
    {
        await SeedAsync();
        var editor = new NeverFixer();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new Fix(ledger, config: config, workspace: new FakeWorkspace(), fileFixer: editor)
            .RunAsync(Adapter(), new FixOptions(), null, CancellationToken.None));

        Assert.Contains("source", error.Message);
        Assert.Equal(0, editor.Calls);
    }

    [Fact]
    public async Task SourceChangedDuringWorkerWithoutScanningCannotBePatched()
    {
        var id = await SeedAsync();
        var workspace = new FakeWorkspace();
        var editor = new ResponseFixer(() =>
        {
            tree.Add(Path, "export function Invoice() { return <p>Changed while reviewing</p>; }");
            return new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed contrast", [id], [])), Path);
        });

        var result = await new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor, new FakeContentHasher())
            .RunAsync(Adapter(), new FixOptions(MaxAttempts: 1), null, CancellationToken.None);

        Assert.Equal(0, result.Fixed);
        Assert.Single(result.GaveUp);
        Assert.Empty(workspace.Commits);
        Assert.Empty(ledger.Fixes);
        Assert.Contains(ledger.Analyses, analysis => analysis.Analysis.Error?.Contains("source check failed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CurrentBrowserFindingCanCompleteTheIsolatedRepairWorkflow()
    {
        var id = await SeedAsync();
        var workspace = new FakeWorkspace();
        var editor = new ResponseFixer(() => new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed contrast", [id], [])), Path));

        var result = await new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor, new FakeContentHasher())
            .RunAsync(Adapter(), new FixOptions(MaxAttempts: 1), null, CancellationToken.None);

        Assert.Equal(1, result.Fixed);
        Assert.Empty(result.GaveUp);
        Assert.Single(workspace.Commits);
        Assert.Equal(FixState.Fixed, ledger.Fixes[id].State);
    }

    private async Task<long> SeedAsync()
    {
        tree.Add(Path, "export function Invoice() { return <p>Invoice</p>; }");
        var hash = Assert.Single(tree.Files).KnownHash!;
        var file = new FileRecord(Path, Languages.TypeScript, hash, 10, At, At, At, null, null, null, null, null, null);
        ledger.Files[Path] = file;
        var plan = Assert.Single(UxReview.Plan([file], config.UserExperience));
        var lensHash = Config.HashOf(config.LensesFor([(Path, Languages.TypeScript)]));
        var unit = new Unit(plan.Id, UnitKind.Ux, Path, Fingerprints.Compute(plan.Members), UnitStatus.Done, Fidelity.Full, lensHash, null, null);
        ledger.Units.Add(unit);
        ledger.Members.AddRange(plan.Members);
        var finding = new Finding(Path, 1, 1, Severity.Medium, "ux_readability", "Unreadable balance", "Browser measurement", 1, "user_experience");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, lensHash, At, true, "summary", null), [finding], CancellationToken.None);
        var id = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Id;
        ledger.Verifications[id] = new VerifyResponse(Verdict.Confirmed, "browser confirmed");
        return id;
    }

    private static FakeAgentAdapter Adapter() => new((_, _) => throw new InvalidOperationException("isolated worker must own invocation"));

    private sealed class NeverFixer : IFileFixer
    {
        public int Calls { get; private set; }

        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("browser repair should not have started");
        }
    }

    private sealed class ResponseFixer(Func<FileFixEdit> response) : IFileFixer
    {
        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken) => Task.FromResult(response());
    }
}
