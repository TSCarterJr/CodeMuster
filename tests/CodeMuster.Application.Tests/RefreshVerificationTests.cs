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
