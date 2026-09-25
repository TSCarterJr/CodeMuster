using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class RefreshVerificationTests
{
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeContentHasher hasher = new();
    private readonly FakeClock clock = new();
    private static Config Enabled => Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true, BaseUrl = "http://localhost:3000" } };

    [Fact]
    public async Task UnchangedUxVerificationKeepsTheSourceContextAndCompletedStatus()
    {
        var verify = await SeedUxAsync();

        await new RefreshVerification(ledger, tree, Enabled, hasher).RunAsync(CancellationToken.None);

        Assert.Equal(verify.Fingerprint, ledger.Units.Single(unit => unit.Id == verify.Id).Fingerprint);
        Assert.Equal(UnitStatus.Done, ledger.Units.Single(unit => unit.Id == verify.Id).Status);
        var source = ledger.Units.Single(unit => unit.Kind == UnitKind.Ux);
        Assert.Equal(source.Fingerprint, ledger.Units.Single(unit => unit.Id == verify.Id).Fingerprint);
    }

    [Fact]
    public async Task BackendChangesRequireScanBeforeBrowserVerificationCanRefresh()
    {
        var verify = await SeedUxAsync();
        tree.Add("api/Invoice.cs", "class Invoice { bool CanCharge => false; }");

        var error = await Assert.ThrowsAsync<JsonException>(() => new RefreshVerification(ledger, tree, Enabled, hasher).RunAsync(CancellationToken.None));

        Assert.Contains("scan", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(verify, ledger.Units.Single(unit => unit.Id == verify.Id));
    }

    [Fact]
    public async Task ScannedBackendChangesReopenVerificationWithTheNewSourceContext()
    {
        var verify = await SeedUxAsync();
        tree.Add("api/Invoice.cs", "class Invoice { bool CanCharge => false; }");
        await ScanAsync();
        var source = ledger.Units.Single(unit => unit.Kind == UnitKind.Ux);

        await new RefreshVerification(ledger, tree, Enabled, hasher).RunAsync(CancellationToken.None);

        var refreshed = ledger.Units.Single(unit => unit.Id == verify.Id);
        Assert.Equal(source.Fingerprint, refreshed.Fingerprint);
        Assert.NotEqual(verify.Fingerprint, refreshed.Fingerprint);
        Assert.Equal(UnitStatus.Pending, refreshed.Status);
    }

    [Fact]
    public async Task ChangedRuntimeSettingsRequireScanInsteadOfReusingOldBrowserScope()
    {
        await SeedUxAsync();
        var changed = Enabled with { UserExperience = Enabled.UserExperience with { BaseUrl = "http://localhost:4000" } };

        var error = await Assert.ThrowsAsync<JsonException>(() => new RefreshVerification(ledger, tree, changed, hasher).RunAsync(CancellationToken.None));

        Assert.Contains("scan", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledOrExcludedUxCannotRefreshItsBrowserVerification(bool disabled)
    {
        await SeedUxAsync();
        var settings = disabled ? Enabled.UserExperience with { Enabled = false } : Enabled.UserExperience with { Exclude = ["web/**"] };
        var changed = Enabled with { UserExperience = settings };

        await Assert.ThrowsAsync<JsonException>(() => new RefreshVerification(ledger, tree, changed, hasher).RunAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(UnitKind.DeadCode, "maintainability", "default")]
    [InlineData(UnitKind.File, "dead_code", "default")]
    [InlineData(UnitKind.File, "maintainability", "dead_code")]
    public async Task DeterministicUnusedCandidatesDoNotAcquireVerificationUnits(UnitKind kind, string category, string lens)
    {
        tree.Add("api/Invoice.cs", "class Invoice { }");
        var source = new Unit("source", kind, "api/Invoice.cs", "source-fp", UnitStatus.Done, Fidelity.Full, "lens", "candidate", "source-fp");
        await ledger.UpsertUnitsAsync([source], [new UnitMember(source.Id, source.Key, null, "hash", 0)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, source.Fingerprint, "lens", "now", true, "candidate", null),
            [new Finding(source.Key, 1, 1, Severity.Info, category, "Unused candidate", "Bounded map only", 0.5, lens)], CancellationToken.None);

        await new RefreshVerification(ledger, tree, Enabled, hasher).RunAsync(CancellationToken.None);

        Assert.DoesNotContain(ledger.Units, unit => unit.Kind == UnitKind.Verify);
    }

    [Fact]
    public async Task ExistingDeadCodeVerificationIsRetiredWithoutDeletingItsHistory()
    {
        tree.Add("api/Invoice.cs", "class Invoice { }");
        var source = new Unit("source", UnitKind.DeadCode, "api/Invoice.cs", "source-fp", UnitStatus.Done, Fidelity.Full, "lens", "candidate", "source-fp");
        await ledger.UpsertUnitsAsync([source], [new UnitMember(source.Id, source.Key, null, "hash", 0)], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, source.Fingerprint, "lens", "now", true, "candidate", null),
            [new Finding(source.Key, 1, 1, Severity.Info, "dead_code", "Unused candidate", "Bounded map only", 0.5, "dead_code")], CancellationToken.None);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        var verify = new Unit(UnitIds.Verify(finding.Id), UnitKind.Verify, source.Key, source.Fingerprint, UnitStatus.Pending, Fidelity.Full, null, null, null);
        await ledger.UpsertUnitsAsync([verify], [new UnitMember(verify.Id, source.Key, null, "hash", 0)], CancellationToken.None);

        await new RefreshVerification(ledger, tree, Enabled, hasher).RunAsync(CancellationToken.None);

        Assert.Equal(UnitStatus.Retired, ledger.Units.Single(unit => unit.Id == verify.Id).Status);
        Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnchangedSliceFindingKeepsItsCompletedCheckThroughRefreshAndTheNextScan()
    {
        var verify = await VerifiedSliceFindingAsync();
        var members = await ledger.GetMembersAsync([verify.Id], CancellationToken.None);

        await RefreshAsync();

        Assert.Equal((verify.Fingerprint, UnitStatus.Done), Check(verify.Id));
        Assert.Equal(members, await ledger.GetMembersAsync([verify.Id], CancellationToken.None));
        Assert.Equal(0, (await SliceScanAsync()).UnitsStale);
        Assert.Equal((verify.Fingerprint, UnitStatus.Done), Check(verify.Id));
    }

    [Fact]
    public async Task FileVerifiedWhileModifiedKeepsItsCheckWhenCommittedUnchanged()
    {
        const string path = "src/A.cs";
        const string content = "class A { int x; }";
        tree.Add(path, content);
        hasher.Hashes[path] = tree.Files.Single(f => f.Path == path).KnownHash!;
        tree.AddDirty(path, content);
        await new Scan(ledger, tree, hasher, clock, Config.Default).RunAsync(CancellationToken.None);
        var verify = await VerifiedFindingAsync(UnitIds.File(path), path);

        await RefreshAsync();
        Assert.Equal((verify.Fingerprint, UnitStatus.Done), Check(verify.Id));
        tree.Add(path, content);
        await RefreshAsync();

        Assert.Equal((verify.Fingerprint, UnitStatus.Done), Check(verify.Id));
        Assert.Equal(0, (await new Scan(ledger, tree, hasher, clock, Config.Default).RunAsync(CancellationToken.None)).UnitsStale);
    }

    [Fact]
    public async Task EditingReportedCodeAfterScanReopensItsCheckOverTheWholeCurrentFiles()
    {
        var verify = await VerifiedSliceFindingAsync();
        tree.Add(MixedRepo.ControllerPath, "content of the controller, edited");

        await RefreshAsync();

        var (fingerprint, status) = Check(verify.Id);
        Assert.Equal(UnitStatus.Pending, status);
        Assert.NotEqual(verify.Fingerprint, fingerprint);
        var members = await ledger.GetMembersAsync([verify.Id], CancellationToken.None);
        Assert.Contains(members, m => m.Path == MixedRepo.ControllerPath && m.MemberHash == tree.Files.Single(f => f.Path == MixedRepo.ControllerPath).KnownHash);
        Assert.All(members, m => Assert.Null(m.Symbol));
    }

    private Task RefreshAsync() => new RefreshVerification(ledger, tree, Config.Default, hasher).RunAsync(CancellationToken.None);

    private (string Fingerprint, UnitStatus Status) Check(string id) =>
        ledger.Units.Where(unit => unit.Id == id).Select(unit => (unit.Fingerprint, unit.Status)).Single();

    private Task<ScanResult> SliceScanAsync() =>
        new Scan(ledger, tree, hasher, clock, Config.Default, [new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp()), new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript())], "/repos/mixed-repo")
            .RunAsync(CancellationToken.None);

    private async Task<Unit> VerifiedSliceFindingAsync()
    {
        MixedRepo.AddTo(tree);
        await SliceScanAsync();
        return await VerifiedFindingAsync(UnitIds.Slice(MixedRepo.ControllerListQuotes), MixedRepo.ControllerPath);
    }

    private async Task<Unit> VerifiedFindingAsync(string sourceId, string path)
    {
        var source = ledger.Units.Single(unit => unit.Id == sourceId);
        var done = new Done(ledger, clock, Config.Default);
        var finding = new Finding(path, 1, 1, Severity.High, "correctness", "claim", "evidence", 0.9, "default");
        Assert.Equal(DoneOutcome.Recorded, (await done.RunAsync(source.Id, source.Fingerprint, AnalysisResponseJson.Serialize(new AnalysisResponse("summary", [finding])), CancellationToken.None)).Outcome);
        var verify = ledger.Units.Single(unit => unit.Kind == UnitKind.Verify && unit.Status == UnitStatus.Pending);
        Assert.Equal(DoneOutcome.Recorded, (await done.RunAsync(verify.Id, verify.Fingerprint, """{"verdict":"confirmed","reason":"The defect is still there."}""", CancellationToken.None)).Outcome);
        return Assert.Single(ledger.Units, unit => unit.Id == verify.Id && unit.Status == UnitStatus.Done);
    }

    private Task<ScanResult> ScanAsync() => new Scan(ledger, tree, hasher, clock, Enabled).RunAsync(CancellationToken.None);

    private async Task<Unit> SeedUxAsync()
    {
        tree.Add("web/Invoice.tsx", "export default () => <button>Charge</button>;");
        tree.Add("api/Invoice.cs", "class Invoice { bool CanCharge => true; }");
        await ScanAsync();
        var source = ledger.Units.Single(unit => unit.Kind == UnitKind.Ux);
        var members = await ledger.GetMembersAsync([source.Id], CancellationToken.None);
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, source.Fingerprint, "lens", "now", true, "browser review", null),
            [new Finding(source.Key, 1, 1, Severity.Medium, "ux_readability", "Low contrast", "Browser sample", 1, UxReview.Id)], CancellationToken.None);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        var plan = PlannedUnit.Verify(finding, members, source.Fidelity);
        var verify = new Unit(plan.Id, UnitKind.Verify, plan.Key, Fingerprints.Compute(plan.Members), UnitStatus.Pending, plan.Fidelity, null, null, null);
        await ledger.UpsertUnitsAsync([verify], plan.Members, CancellationToken.None);
        await ledger.RecordVerificationAsync(new Analysis(verify.Id, verify.Fingerprint, "lens", "now", true, "confirmed", null), finding.Id,
            new VerifyResponse(Verdict.Confirmed, "Observed low contrast"), CancellationToken.None);
        return ledger.Units.Single(unit => unit.Id == verify.Id);
    }
}
