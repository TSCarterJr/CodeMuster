using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class HeadlessTests
{
    private const string PlantedClaim = "planted by the fake adapter: café → naïve";

    [Fact]
    public async Task RunWithFakeAgent_CompletesEveryUnit_AndReportShowsPlantedFindings()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        var template = Path.Combine(repo.Root, "template.json");
        var planted = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0] with { Claim = PlantedClaim, Severity = Severity.Medium };
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("fake summary", [planted])));
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template };

        var run = await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake", "-j", "3");
        Assert.Equal(0, run.ExitCode);
        var status = await CliProcess.RunAsync(repo.Root, "status");
        var match = Regex.Match(status.Stdout, @"^analyzed (\d+)/(\d+) at", RegexOptions.Multiline);
        Assert.True(match.Success, status.Stdout);
        Assert.Equal(match.Groups[2].Value, match.Groups[1].Value);
        var total = int.Parse(match.Groups[2].Value);
        var progress = Regex.Matches(run.Stdout, "^(\\d+)/\\d+ file:", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(Enumerable.Range(1, total), progress);
        Assert.Equal($"completed {total} unit(s), 0 gave up", run.Stdout.TrimEnd().Split('\n')[^1].TrimEnd('\r'));

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Equal(0, report.ExitCode);
        Assert.StartsWith("# CodeMuster report", report.Stdout);
        Assert.Contains($"analyzed {total}/{total} units at", report.Stdout);
        Assert.Contains("### medium (" + total + ")", report.Stdout);
        Assert.Contains(PlantedClaim, report.Stdout);
        Assert.Contains("| src/MixedRepo.Api/Program.cs | done | fake summary |", report.Stdout);

        var reportPath = Path.Combine(repo.Root, "audit.md");
        var written = await CliProcess.RunAsync(repo.Root, "report", "--out", reportPath);
        Assert.Equal(0, written.ExitCode);
        Assert.Equal(report.Stdout, await File.ReadAllTextAsync(reportPath));

        var again = await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake");
        Assert.Equal(0, again.ExitCode);
        Assert.Contains("completed 0 unit(s)", again.Stdout);

        var forced = await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake", "--force", "-j", "2");
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains($"completed {total} unit(s)", forced.Stdout);
    }

    [Fact]
    public async Task RunWithDefaultFake_RecordsEmptyFindings()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake");
        Assert.Equal(0, run.ExitCode);

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("## Findings (0)", report.Stdout);
        Assert.Contains("| done | fake analysis |", report.Stdout);
    }

    [Fact]
    public async Task RunWithMissingFakeTemplate_PrintsOneLineError_InsteadOfAStackTrace()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = Path.Combine(repo.Root, "nope.json") };

        var run = await CliProcess.RunAsync(repo.Root, environment, "run", "--agent", "fake");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("nope.json", run.Stderr);
        Assert.DoesNotContain("Unhandled exception", run.Stderr);
        Assert.Single(run.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task RunWithUnknownAgent_FailsBeforeTouchingUnits()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "gpt5");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("fake", run.Stderr);
        Assert.Contains("analyzed 0/", (await CliProcess.RunAsync(repo.Root, "status")).Stdout);
    }

    [Fact]
    public async Task SkillInstall_WritesTheEmbeddedSkill_AndIsIdempotent()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var expected = await File.ReadAllTextAsync(Path.Combine(TempRepo.FindRepoRoot(), "skill", "SKILL.md"));
        var target = Path.Combine(repo.Root, ".claude", "skills", "codemuster", "SKILL.md");

        var install = await CliProcess.RunAsync(repo.Root, "skill", "install", "--for", "claude");
        Assert.Equal(0, install.ExitCode);
        Assert.Contains("installed skill to", install.Stdout);
        Assert.Equal(expected.ReplaceLineEndings("\n"), (await File.ReadAllTextAsync(target)).ReplaceLineEndings("\n"));
        var stamp = File.GetLastWriteTimeUtc(target);

        var again = await CliProcess.RunAsync(repo.Root, "skill", "install", "--for", "claude");
        Assert.Equal(0, again.ExitCode);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(target));
    }

    [Fact]
    public async Task SkillInstallGlobal_WorksOutsideARepo_AndUsesThePlatformHomeVariable()
    {
        var outside = Path.Combine(Path.GetTempPath(), "codemuster-e2e-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(outside, "home");
        var otherHome = Path.Combine(outside, "other-home");
        var work = Path.Combine(outside, "work");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(otherHome);
        Directory.CreateDirectory(work);
        try
        {
            var environment = new Dictionary<string, string>
            {
                ["USERPROFILE"] = OperatingSystem.IsWindows() ? home : otherHome,
                ["HOME"] = OperatingSystem.IsWindows() ? otherHome : home,
                ["GIT_CEILING_DIRECTORIES"] = outside,
            };

            var install = await CliProcess.RunAsync(work, environment, "skill", "install", "--for", "opencode", "--global");

            Assert.Equal(0, install.ExitCode);
            Assert.True(File.Exists(Path.Combine(home, ".config", "opencode", "skills", "codemuster", "SKILL.md")), install.Stdout + install.Stderr);
            Assert.False(Directory.Exists(Path.Combine(otherHome, ".config", "opencode")));
            Assert.Empty(Directory.GetFileSystemEntries(work));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("run --agent fake -j 0")]
    [InlineData("run --agent fake --yes")]
    [InlineData("skill install")]
    [InlineData("skill install --for gpt5")]
    [InlineData("skill remove --for claude")]
    [InlineData("report extra")]
    [InlineData("run --agent fake --lens tenancy")]
    public async Task BadUsage_ForNewVerbs_Exits2(string arguments)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), arguments.Split(' '));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage: codemuster", result.Stderr);
    }
}
