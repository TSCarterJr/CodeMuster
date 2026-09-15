using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class FixReviewSafetyTests
{
    private const string At = "2026-09-15T00:00:00.0000000Z";
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly Config config = Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true, BaseUrl = "http://localhost:3000" } };

    [Theory]
    [InlineData("dead_code", "dead_code")]
    [InlineData("default", "dead_code")]
    [InlineData("dead_code", "correctness")]
    [InlineData(" DEAD_CODE ", "correctness")]
    [InlineData("default", " DEAD_CODE ")]
    [InlineData("user_experience", "ux_recommendation")]
    [InlineData("default", " UX_RECOMMENDATION ")]
    public async Task ReportOnlyFindingsAreExcludedEvenWhenAlreadyConfirmed(string lens, string category)
    {
        await SeedAsync(lens, category);

        var plan = await new Fix(ledger, config: config).PlanAsync(CancellationToken.None);

        Assert.Equal(new FixPlan(0, 0, 0), plan);
        Assert.DoesNotContain(ledger.Units, unit => unit.Kind == UnitKind.Fix);
    }

    [Fact]
    public async Task APreviouslyPlannedReportOnlyFixIsRetired()
    {
        await SeedAsync("dead_code", "dead_code");
        var id = UnitIds.Fix("web/Invoice.tsx");
        var member = new UnitMember(id, "web/Invoice.tsx", null, "hash", 0);
        ledger.Units.Add(new Unit(id, UnitKind.Fix, "web/Invoice.tsx", Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.Add(member);

        await new Fix(ledger, config: config).PlanAsync(CancellationToken.None);

        Assert.Equal(UnitStatus.Retired, ledger.Units.Single(unit => unit.Id == id).Status);
    }

    [Theory]
    [InlineData("user_experience", "ux_readability")]
    [InlineData("user_experience", "ux_workflow")]
    [InlineData("default", " UX_READABILITY ")]
    [InlineData(" USER_EXPERIENCE ", "correctness")]
    public async Task ChangedSourceCannotReuseBrowserFindingForRepair(string lens, string category)
    {
        var (source, _) = await SeedAsync(lens, category);
        ledger.Units[ledger.Units.FindIndex(unit => unit.Id == source.Id)] = source with { Fingerprint = "changed" };

        var plan = await new Fix(ledger, config: config).PlanAsync(CancellationToken.None);

        Assert.Equal(new FixPlan(0, 0, 0), plan);
    }

    [Theory]
    [InlineData("ux_readability")]
    [InlineData("ux_workflow")]
    public async Task CurrentMeasuredOrObservedUxCanBePlanned(string category)
    {
        await SeedAsync("user_experience", category);

        Assert.Equal(new FixPlan(1, 1, 1), await new Fix(ledger, config: config).PlanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARemovedSourceCannotAuthorizeBrowserBasedRepairs()
    {
        var (source, _) = await SeedAsync("user_experience", "ux_readability");
        ledger.Units.RemoveAll(unit => unit.Id == source.Id);

        Assert.Equal(new FixPlan(0, 0, 0), await new Fix(ledger, config: config).PlanAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(UnitStatus.Pending)]
    [InlineData(UnitStatus.Stale)]
    [InlineData(UnitStatus.Failed)]
    [InlineData(UnitStatus.Retired)]
    public async Task IncompleteBrowserReviewCannotAuthorizeRepairsEvenWithAnUnchangedFingerprint(UnitStatus status)
    {
        var (source, _) = await SeedAsync("user_experience", "ux_readability");
        ledger.Units[ledger.Units.FindIndex(unit => unit.Id == source.Id)] = source with { Status = status };

        Assert.Equal(new FixPlan(0, 0, 0), await new Fix(ledger, config: config).PlanAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(UnitKind.DeadCode, false)]
    [InlineData(UnitKind.Ux, true)]
    public async Task SourceUnitKindProtectsMislabeledHistoricalFindings(UnitKind kind, bool stale)
    {
        var (source, _) = await SeedAsync("default", "correctness");
        ledger.Units[ledger.Units.FindIndex(unit => unit.Id == source.Id)] = source with { Kind = kind, Fingerprint = stale ? "changed" : source.Fingerprint };

        Assert.Equal(new FixPlan(0, 0, 0), await new Fix(ledger, config: config).PlanAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BrowserEvidenceThatBecomesStaleWhileTheWorkerRunsCannotApplyAPatch()
    {
        var (source, id) = await SeedAsync("user_experience", "ux_readability");
        var editor = new TrackingFixer(_ =>
        {
            ledger.Units[ledger.Units.FindIndex(unit => unit.Id == source.Id)] = source with { Fingerprint = "changed" };
            return new FileFixEdit(FixResponseJson.Serialize(new FixResponse("changed contrast", [id], [])), "web/Invoice.tsx");
        });
        var workspace = new FakeWorkspace();

        var result = await new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor, new FakeContentHasher())
            .RunAsync(Adapter(), new FixOptions(MaxAttempts: 1), null, CancellationToken.None);

        Assert.Equal(0, result.Fixed);
        Assert.Single(result.GaveUp);
        Assert.Empty(ledger.Fixes);
        Assert.Empty(workspace.Commits);
        Assert.True(workspace.Clean);
        Assert.Contains(ledger.Analyses, entry => entry.Analysis.Error?.Contains("eligibility changed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ReportOnlyRunNeverStashesOrStartsAnEditor()
    {
        await SeedAsync("dead_code", "dead_code");
        var workspace = new FakeWorkspace { Clean = false };
        var editor = new TrackingFixer(_ => throw new InvalidOperationException("report-only code must not reach an editor"));

        var result = await new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor)
            .RunAsync(Adapter(), new FixOptions(Stash: true), null, CancellationToken.None);

        Assert.Equal(0, result.Fixed);
        Assert.Empty(editor.Packs);
        Assert.Equal(0, workspace.Stashes);
        Assert.Empty(workspace.Commits);
        Assert.False(workspace.Clean);
    }

    [Fact]
    public async Task MixedFileRepairsOnlyEligibleFindings()
    {
        var (source, _) = await SeedAsync("dead_code", "dead_code");
        var good = new Finding(source.Key, 2, 2, Severity.High, "correctness", "fixable logic defect", "code evidence", 1, "default");
        var recommendation = good with { LineStart = 3, LineEnd = 3, Category = "ux_recommendation", LensId = "user_experience", Claim = "report-only layout advice" };
        var dead = good with { LineStart = 1, LineEnd = 1, Category = "dead_code", LensId = "dead_code", Claim = "report-only unused candidate" };
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, source.Fingerprint, "lens", At, true, "summary", null), [dead, good, recommendation], CancellationToken.None);
        var findings = await ledger.GetCurrentFindingsAsync(CancellationToken.None);
        foreach (var finding in findings) ledger.Verifications[finding.Id] = new VerifyResponse(Verdict.Confirmed, "confirmed");
        var goodId = findings.Single(finding => finding.Finding.Category == "correctness").Id;
        var editor = new TrackingFixer(_ => new FileFixEdit(FixResponseJson.Serialize(new FixResponse("fixed eligible defect", [goodId], [])), "web/Invoice.tsx"));
        var workspace = new FakeWorkspace();

        var result = await new Fix(ledger, tree, new FakeClock(), config, workspace, null, editor)
            .RunAsync(Adapter(), new FixOptions(MaxAttempts: 1), null, CancellationToken.None);

        Assert.Equal(1, result.Fixed);
        Assert.Empty(result.GaveUp);
        Assert.Contains("fixable logic defect", Assert.Single(editor.Packs));
        Assert.DoesNotContain("report-only unused candidate", editor.Packs[0]);
        Assert.DoesNotContain("report-only layout advice", editor.Packs[0]);
        Assert.Single(ledger.Fixes);
        Assert.True(ledger.Fixes.ContainsKey(goodId));
    }

    private async Task<(Unit Source, long FindingId)> SeedAsync(string lens, string category)
    {
        const string path = "web/Invoice.tsx";
        tree.Add(path, "export function Invoice() { return <p>Invoice</p>; }");
        var hash = Assert.Single(tree.Files).KnownHash!;
        var file = new FileRecord(path, Languages.TypeScript, hash, 10, At, At, At, null, null, null, null, null, null);
        var browser = string.Equals(lens.Trim(), "user_experience", StringComparison.OrdinalIgnoreCase) || category.Trim().StartsWith("ux_", StringComparison.OrdinalIgnoreCase);
        var plan = browser ? Assert.Single(UxReview.Plan([file], config.UserExperience))
            : new PlannedUnit(UnitIds.File(path), UnitKind.File, path, Fidelity.Full, [new UnitMember(UnitIds.File(path), path, null, hash, 0)]);
        var id = plan.Id;
        var lensHash = Config.HashOf(config.LensesFor([(path, Languages.TypeScript)]));
        var unit = new Unit(id, plan.Kind, path, Fingerprints.Compute(plan.Members), UnitStatus.Done, Fidelity.Full, lensHash, null, null);
        ledger.Units.Add(unit);
        ledger.Members.AddRange(plan.Members);
        ledger.Files[path] = file;
        var finding = new Finding(path, 1, 1, Severity.Low, category, "claim", "evidence", 1, lens);
        await ledger.RecordAnalysisAsync(new Analysis(id, unit.Fingerprint, lensHash, At, true, "summary", null), [finding], CancellationToken.None);
        var recorded = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        ledger.Verifications[recorded.Id] = new VerifyResponse(Verdict.Confirmed, "confirmed");
        return (unit, recorded.Id);
    }

    private static FakeAgentAdapter Adapter() => new((_, _) => throw new InvalidOperationException("isolated editor owns the invocation"));

    private sealed class TrackingFixer(Func<string, FileFixEdit> edit) : IFileFixer
    {
        public List<string> Packs { get; } = [];

        public Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
        {
            Packs.Add(pack);
            return Task.FromResult(edit(pack));
        }
    }
}
