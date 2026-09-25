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
    public void Pnpm_marks_a_package_direct_from_its_resolve_paths_because_it_leaves_findings_paths_empty()
    {
        Assert.All(AdvisoryMapJson.Parse(Fixture("pnpm-audit.json")), package => Assert.True(package.Direct));

        var found = AdvisoryMapJson.Parse("""
            {"actions":[{"action":"review","module":"minimist","resolves":[{"id":1,"path":".>minimist"},{"id":2,"path":".>mkdirp>minimist"}]}],
             "advisories":{
               "1":{"id":1,"module_name":"minimist","severity":"high","findings":[{"version":"0.0.8","paths":[]}]},
               "2":{"id":2,"module_name":"minimist","severity":"high","findings":[{"version":"1.2.0","paths":[]}]}}}
            """);

        Assert.Equal([true, false], found.Select(package => package.Direct));
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

    [Fact]
    public void Npm_rejects_its_error_envelope_rather_than_reporting_a_clean_manifest()
    {
        var offline = Assert.Throws<InvalidOperationException>(() => NpmAuditJson.Parse(Fixture("npm-offline.json")));
        Assert.Contains("ECONNREFUSED 127.0.0.1:9", offline.Message, StringComparison.Ordinal);
        var noLockfile = Assert.Throws<InvalidOperationException>(() => NpmAuditJson.Parse(Fixture("npm-enolock.json")));
        Assert.Contains("requires an existing lockfile", noLockfile.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => NpmAuditJson.Parse("""{"vulnerabilities": {}}"""));
    }

    [Fact]
    public void Pnpm_rejects_its_error_envelope_and_output_without_advisories()
    {
        var offline = Assert.Throws<InvalidOperationException>(() => AdvisoryMapJson.Parse(Fixture("pnpm-offline.json")));
        Assert.Contains("ECONNREFUSED 127.0.0.1:9", offline.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => AdvisoryMapJson.Parse("""{"metadata": {}}"""));
    }

    [Fact]
    public void Dotnet_rejects_error_problems_but_accepts_warnings()
    {
        var restore = Assert.Throws<InvalidOperationException>(() => DotnetAuditJson.Parse(Fixture("dotnet-restore-failed.json")));
        Assert.Contains("Restore failed", restore.Message, StringComparison.Ordinal);
        var assets = Assert.Throws<InvalidOperationException>(() => DotnetAuditJson.Parse(Fixture("dotnet-no-assets.json")));
        Assert.Contains("No assets file was found", assets.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DotnetAuditJson.Parse("""{"version": 1}"""));
        Assert.Empty(DotnetAuditJson.Parse(
            """{"version": 1, "problems": [{"project": "/src/app/App.csproj", "level": "warning", "text": "a warning"}], "projects": [{"path": "/src/app/App.csproj"}]}"""));
    }

    [Fact]
    public void Yarn_rejects_output_that_holds_no_audit_report()
    {
        Assert.Throws<InvalidOperationException>(() => YarnAuditJson.Parse(Fixture("yarn-offline.json")));
        var error = Assert.Throws<InvalidOperationException>(() => YarnAuditJson.Parse(
            "{\"type\":\"error\",\"data\":\"Error: https://registry.yarnpkg.com/-/npm/v1/security/audits: tunneling socket could not be established, cause=connect ECONNREFUSED 127.0.0.1:9\"}\n"
            + "{\"type\":\"info\",\"data\":\"Visit https://yarnpkg.com/en/docs/cli/audit for documentation about this command.\"}\n"));
        Assert.Contains("ECONNREFUSED 127.0.0.1:9", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => YarnAuditJson.Parse(
            "➤ YN0001: RequestError: connect ECONNREFUSED 127.0.0.1:9\n    at ClientRequest.<anonymous> (yarn.js:195:14340)\n\n➤ Errors happened when preparing the environment required to run this command.\n"));
    }

    [Fact]
    public void Yarn3_npm_audit_writes_the_advisory_map_and_it_is_read()
    {
        var found = YarnAuditJson.Parse(Fixture("yarn3-audit.json"));

        Assert.Equal(["minimist", "minimist"], found.Select(p => p.Package));
        Assert.Equal("GHSA-xvch-5gv4-984h", Assert.Single(found, p => p.Severity == Severity.Critical).AdvisoryId);
    }
}
