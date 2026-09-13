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
}
