using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DependencyScanTests
{
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeDependencyAuditor auditor = new();

    public DependencyScanTests()
    {
        tree.Add("web/package.json", "{}");
        tree.Add("web/package-lock.json", "{}");
        tree.Add("src/A.cs", "class A { }");
    }

    private Task<ScanResult> ScanAsync(Config? config = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Config.Default, null, "/repo", null, auditor)
            .RunAsync(CancellationToken.None);

    private IReadOnlyList<UnitFinding> FindingsAsync() =>
        ledger.GetCurrentFindingsAsync(CancellationToken.None).GetAwaiter().GetResult();

    private void Vulnerable(params VulnerablePackage[] packages) =>
        auditor.Manifests = [new ManifestVulnerabilities("web/package.json", "npm audit", packages)];

    [Fact]
    public async Task EachAdvisory_BecomesAConfirmedFindingOnItsManifest()
    {
        Vulnerable(
            FakeDependencyAuditor.Package("next", Severity.Critical, "16.3.5"),
            FakeDependencyAuditor.Package("sharp", Severity.High));

        var result = await ScanAsync();

        var unit = Assert.Single(ledger.Units, u => u.Kind == UnitKind.Dependency);
        Assert.Equal("dependency:web/package.json", unit.Id);
        Assert.Equal(UnitStatus.Done, unit.Status);
        var findings = FindingsAsync().Where(f => f.UnitId == unit.Id).OrderBy(f => f.Finding.Severity).ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(Verdict.Confirmed, finding.Verification!.Verdict));
        Assert.All(findings, finding => Assert.Equal("web/package.json", finding.Finding.Path));
        Assert.All(findings, finding => Assert.Equal("dependency", finding.Finding.Category));
        var next = findings[0];
        Assert.Equal(Severity.Critical, next.Finding.Severity);
        Assert.Contains("next", next.Finding.Claim, StringComparison.Ordinal);
        Assert.Contains("GHSA-next", next.Finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("16.3.5", next.Finding.Evidence, StringComparison.Ordinal);
        Assert.Equal(1.0, next.Finding.Confidence);
        Assert.Equal(2, result.Vulnerabilities!.Packages);
        Assert.Equal(1, result.Vulnerabilities.BySeverity[Severity.Critical]);
    }

    [Fact]
    public async Task ALaterScan_ReplacesWhatTheToolSaysNow()
    {
        Vulnerable(FakeDependencyAuditor.Package("next", Severity.Critical));
        await ScanAsync();

        auditor.Manifests = [new ManifestVulnerabilities("web/package.json", "npm audit", [])];
        var result = await ScanAsync();

        Assert.DoesNotContain(FindingsAsync(), f => f.UnitId == "dependency:web/package.json");
        Assert.Equal(0, result.Vulnerabilities!.Packages);
    }

    [Fact]
    public async Task AFailedAudit_KeepsWhatWeKnew_AndSaysWhatBroke()
    {
        Vulnerable(FakeDependencyAuditor.Package("next", Severity.Critical));
        await ScanAsync();

        auditor.Manifests = [];
        auditor.Diagnostics = ["web/package.json: npm could not run (offline); install npm or exclude that folder"];
        var result = await ScanAsync();

        Assert.Single(FindingsAsync(), f => f.UnitId == "dependency:web/package.json");
        Assert.Equal(["web/package.json: npm could not run (offline); install npm or exclude that folder"], result.Vulnerabilities!.Diagnostics);
    }

    [Fact]
    public async Task TurnedOff_NothingRuns()
    {
        Vulnerable(FakeDependencyAuditor.Package("next", Severity.Critical));

        var result = await ScanAsync(Config.Default with { Vulnerabilities = false });

        Assert.Equal(0, auditor.Calls);
        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Dependency);
        Assert.Null(result.Vulnerabilities);
    }

    [Fact]
    public async Task DependencyFindings_AreNeverHandedToAModelToVerify()
    {
        Vulnerable(FakeDependencyAuditor.Package("next", Severity.Critical));

        await ScanAsync();
        await ScanAsync();

        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Verify);
    }

    [Fact]
    public async Task LockfilesReachTheAudit_EvenThoughTheyAreNeverAnalyzedAsCode()
    {
        tree.Add("app/pnpm-lock.yaml", "lockfile");
        tree.Add("app/package.json", "{}");

        await ScanAsync();

        Assert.Contains("web/package-lock.json", auditor.Paths);
        Assert.Contains("app/pnpm-lock.yaml", auditor.Paths);
        Assert.Contains("web/package.json", auditor.Paths);
    }

    [Fact]
    public async Task AnExcludedFolderIsNeverAudited()
    {
        tree.Add("mobile/package.json", "{}");
        tree.Add("mobile/package-lock.json", "lockfile");

        await ScanAsync(Config.Default with { Exclude = ["mobile/**"] });

        Assert.DoesNotContain("mobile/package.json", auditor.Paths);
        Assert.DoesNotContain("mobile/package-lock.json", auditor.Paths);
    }
}
