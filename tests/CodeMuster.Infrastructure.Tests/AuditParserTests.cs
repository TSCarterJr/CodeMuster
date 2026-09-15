using CodeMuster.Domain;
using CodeMuster.Infrastructure.Audits;

namespace CodeMuster.Infrastructure.Tests;

public class AuditParserTests
{
    [Fact]
    public void YarnBerryReadsCapturedAdvisoryTrees()
    {
        var found = YarnAuditJson.Parse(Fixture("yarn-berry.json"));
        Assert.Equal(2, found.Count);
        Assert.All(found, p => Assert.Equal("minimist", p.Package));
        var critical = Assert.Single(found, p => p.Severity == Severity.Critical);
        Assert.Equal("GHSA-xvch-5gv4-984h", critical.AdvisoryId);
        Assert.Equal("<0.2.4", critical.VulnerableVersions);
        Assert.True(critical.Direct);
        Assert.Null(critical.FixedVersion);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "audits", name));

    [Fact]
    public void Npm_reads_every_advisory_with_its_fix()
    {
        var found = NpmAuditJson.Parse(Fixture("npm-audit.json"));

        Assert.Equal(7, found.Count);
        var next = Assert.Single(found, package => package.AdvisoryId == "GHSA-p293-qw3h-jr36");
        Assert.Equal("next", next.Package);
        Assert.Equal(Severity.Critical, next.Severity);
        Assert.Equal("16.3.5", next.FixedVersion);
        Assert.Equal("16.0.0 - 16.3.2", next.VulnerableVersions);
        Assert.Contains("Remote Code Execution", next.Title, StringComparison.Ordinal);
        Assert.True(next.Direct);
        Assert.Equal(2, found.Count(package => package.Package == "next"));
        Assert.False(Assert.Single(found, package => package.Package == "sharp").Direct);
        Assert.Equal(
            [Severity.Critical, Severity.Critical, Severity.High, Severity.High, Severity.High, Severity.Medium, Severity.Medium],
            found.Select(package => package.Severity).Order());
    }

    [Fact]
    public void Pnpm_reads_the_older_advisory_map()
    {
        var found = AdvisoryMapJson.Parse(Fixture("pnpm-audit.json"));

        Assert.Equal(["minimist", "minimist"], found.Select(p => p.Package));
        var critical = Assert.Single(found, package => package.Severity == Severity.Critical);
        Assert.Equal("GHSA-xvch-5gv4-984h", critical.AdvisoryId);
        Assert.Equal(">=1.2.6", critical.FixedVersion);
        Assert.Equal(">=1.0.0 <1.2.6", critical.VulnerableVersions);
    }

    [Fact]
    public void Yarn_reads_one_object_per_line_and_ignores_the_summary()
    {
        var found = YarnAuditJson.Parse(Fixture("yarn-audit.json"));

        Assert.Equal(2, found.Count);
        Assert.All(found, package => Assert.Equal("minimist", package.Package));
        Assert.Contains(found, package => package.Severity == Severity.Critical && package.FixedVersion == ">=1.2.6");
    }

    [Fact]
    public void Dotnet_reads_each_project_and_says_nothing_about_a_fix()
    {
        var found = DotnetAuditJson.Parse(Fixture("dotnet-vulnerable.json"));

        Assert.Equal(3, found.Count);
        var newtonsoft = Assert.Single(found, entry => entry.Package.Package == "Newtonsoft.Json");
        Assert.EndsWith("vuln.csproj", newtonsoft.Project.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Equal(Severity.High, newtonsoft.Package.Severity);
        Assert.Equal("GHSA-5crp-9r3c-p9vr", newtonsoft.Package.AdvisoryId);
        Assert.Equal("12.0.1", newtonsoft.Package.VulnerableVersions);
        Assert.Null(newtonsoft.Package.FixedVersion);
        Assert.True(newtonsoft.Package.Direct);
    }

    [Fact]
    public void A_clean_report_yields_nothing()
    {
        Assert.Empty(DotnetAuditJson.Parse(Fixture("dotnet-clean.json")));
        Assert.Empty(NpmAuditJson.Parse("""{"auditReportVersion": 2, "vulnerabilities": {}, "metadata": {}}"""));
        Assert.Empty(AdvisoryMapJson.Parse("""{"advisories": {}, "metadata": {}}"""));
        Assert.Empty(YarnAuditJson.Parse("{\"type\":\"auditSummary\",\"data\":{}}\n"));
    }
}
