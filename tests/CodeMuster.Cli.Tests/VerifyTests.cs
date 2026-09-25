using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class VerifyTests
{
    [Fact]
    public async Task Verify_RefutesTheDoubtfulPlantedFinding_AndTheReportDropsItUnlessAskedToIncludeIt()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        var sample = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0];
        var template = Path.Combine(repo.Root, "template.json");
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("planted",
            [sample with { Claim = "kept claim" }, sample with { Claim = "doubtful claim", Confidence = 0.2 }])));
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template };

        var analyze = await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake", "--kind", "file", "-j", "4");
        Assert.Equal(0, analyze.ExitCode);
        var files = int.Parse(Regex.Match(analyze.Stdout, @"^completed (\d+) unit\(s\), 0 gave up\r?$", RegexOptions.Multiline).Groups[1].Value);
        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.Contains($"\nfile {files}/{files}\nverify 0/{2 * files}\n", status);

        var verify = await CliProcess.RunAsync(repo.Root, environment, "verify", "--agent", "fake", "-j", "4");

        Assert.Equal(0, verify.ExitCode);
        Assert.Contains($"completed {2 * files} unit(s), 0 gave up", verify.Stdout);
        Assert.DoesNotMatch(new Regex("^\\d+/\\d+ file:", RegexOptions.Multiline), verify.Stdout);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains($"## Findings ({files}, {files} refuted not shown)", report.Stdout);
        Assert.Contains("kept claim", report.Stdout);
        Assert.DoesNotContain("doubtful claim", report.Stdout);
        var full = await CliProcess.RunAsync(repo.Root, "report", "--include-refuted");
        Assert.Contains($"## Findings ({2 * files})", full.Stdout);
        Assert.Contains("doubtful claim", full.Stdout);
        Assert.Contains("  refuted: fake verification", full.Stdout);
    }

    [Fact]
    public async Task Verify_and_scan_redo_no_check_until_the_code_really_changes()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        var sample = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0];
        var template = Path.Combine(repo.Root, "template.json");
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [sample with { Confidence = 0.9 }])));
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template };
        Assert.Contains("5 slices, 3 orphans", (await CliProcess.RunAsync(repo.Root, "scan")).Stdout);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake", "-j", "4")).ExitCode);
        Assert.EndsWith("\ncomplete", await StatusAsync(repo));

        Assert.Equal(0, await VerifiedAsync(repo, environment));
        Assert.Contains(": 0 new, 0 stale,", (await CliProcess.RunAsync(repo.Root, "scan")).Stdout);
        Assert.EndsWith("\ncomplete", await StatusAsync(repo));
        Assert.Equal(0, await VerifiedAsync(repo, environment));

        var quote = Path.Combine(repo.Root, "src", "MixedRepo.Api", "Data", "Quote.cs");
        await File.AppendAllTextAsync(quote, "// reviewed\n");
        Assert.NotEqual(0, await VerifiedAsync(repo, environment));
        Assert.Equal(0, await VerifiedAsync(repo, environment));
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "reviewed", "--", "src/MixedRepo.Api/Data/Quote.cs");
        Assert.Equal(0, await VerifiedAsync(repo, environment));
    }

    [Fact]
    public async Task Checks_verified_after_an_edit_but_before_the_next_scan_are_not_verified_again_after_it()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        var sample = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0];
        var template = Path.Combine(repo.Root, "template.json");
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [sample with { Confidence = 0.9 }])));
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template };
        Assert.Contains("5 slices, 3 orphans", (await CliProcess.RunAsync(repo.Root, "scan")).Stdout);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake", "-j", "4")).ExitCode);

        // A comment outside every symbol, as fix's commits and hand edits leave: the slices that span this file keep their fingerprints.
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "src", "MixedRepo.Api", "Shared", "Money.cs"), "// reviewed\n");
        Assert.NotEqual(0, await VerifiedAsync(repo, environment));
        Assert.Contains(": 0 new, 0 stale,", (await CliProcess.RunAsync(repo.Root, "scan")).Stdout);

        Assert.Equal(0, await VerifiedAsync(repo, environment));
        Assert.EndsWith("\ncomplete", await StatusAsync(repo));
    }

    private static async Task<int> VerifiedAsync(TempRepo repo, IReadOnlyDictionary<string, string> environment)
    {
        var verify = await CliProcess.RunAsync(repo.Root, environment, "verify", "--agent", "fake", "-j", "4");
        Assert.Equal(0, verify.ExitCode);
        return int.Parse(Regex.Match(verify.Stdout, @"^completed (\d+) unit\(s\), 0 gave up\r?$", RegexOptions.Multiline).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> StatusAsync(TempRepo repo) =>
        (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n").TrimEnd();
}
