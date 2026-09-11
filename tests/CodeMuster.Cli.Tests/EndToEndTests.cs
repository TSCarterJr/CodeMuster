using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class EndToEndTests
{
    [Fact]
    public async Task ScanNextDoneStatus_OnFixtureRepo()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var scan = await CliProcess.RunAsync(repo.Root, "scan");
        Assert.Equal(0, scan.ExitCode);
        Assert.True(File.Exists(Path.Combine(repo.Root, ".codemuster", "ledger.db")));

        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Equal(0, status.ExitCode);
        var total = Regex.Match(status.Stdout, @"^analyzed 0/(\d+) at [0-9a-f]{7}$", RegexOptions.Multiline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(total), status.Stdout);
        Assert.True(int.Parse(total) > 10, status.Stdout);

        var packPath = Path.Combine(repo.Root, "pack.md");
        var next = await CliProcess.RunAsync(repo.Root, "next", "--out", packPath);
        Assert.Equal(0, next.ExitCode);
        var pack = await File.ReadAllTextAsync(packPath);
        var unitId = Regex.Match(pack, @"^- unit: (\S+)$", RegexOptions.Multiline).Groups[1].Value;
        var fingerprint = Regex.Match(pack, @"^- fingerprint: ([0-9a-f]{64})$", RegexOptions.Multiline).Groups[1].Value;
        Assert.StartsWith("file:", unitId);
        Assert.Contains(AnalysisResponseJson.Sample, pack);

        var findingsPath = Path.Combine(repo.Root, "findings.json");
        var finding = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0] with { Path = unitId["file:".Length..] };
        await File.WriteAllTextAsync(findingsPath, AnalysisResponseJson.Serialize(new AnalysisResponse("one file", [finding])));

        var wrongFingerprint = await CliProcess.RunAsync(repo.Root, "done", unitId, "--fingerprint", new string('0', 64), "--findings", findingsPath);
        Assert.Equal(1, wrongFingerprint.ExitCode);

        var done = await CliProcess.RunAsync(repo.Root, "done", unitId, "--fingerprint", fingerprint, "--findings", findingsPath);
        Assert.Equal(0, done.ExitCode);

        status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains($"analyzed 1/{total} at ", status.Stdout);

        var estimate = await CliProcess.RunAsync(repo.Root, "estimate");
        Assert.Equal(0, estimate.ExitCode);
        Assert.Contains("total ~", estimate.Stdout);
    }

    [Fact]
    public async Task NextToStdout_PrintsPack()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "scan");

        var next = await CliProcess.RunAsync(repo.Root, "next", "--batch", "2");

        Assert.Equal(0, next.ExitCode);
        Assert.Equal(2, Regex.Matches(next.Stdout, "^# CodeMuster unit$", RegexOptions.Multiline).Count);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("done")]
    [InlineData("next --batch")]
    public async Task BadUsage_PrintsUsage_AndExits2(string arguments)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), arguments.Split(' '));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage: codemuster", result.Stderr);
    }
}
