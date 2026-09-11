using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class EndToEndTests
{
    [Fact]
    public async Task ScanNextDoneStatus_OnFixtureRepo()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var gated = await CliProcess.RunAsync(repo.Root, "scan");
        Assert.Equal(2, gated.ExitCode);
        Assert.Contains("run `codemuster init`", gated.Stderr);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".codemuster")));

        var init = await CliProcess.RunAsync(repo.Root, "init", "--yes");
        Assert.Equal(0, init.ExitCode);
        Assert.Contains("created .codemuster/config.json", init.Stdout);
        Assert.Contains("already ignores", init.Stdout);

        var scan = await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
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
        Assert.Contains($"analyzed 1/{int.Parse(total) + 1} at ", status.Stdout);
        Assert.Contains("\nverify 0/1\n", status.Stdout.ReplaceLineEndings("\n"));

        next = await CliProcess.RunAsync(repo.Root, "next", "--batch", "100", "--out", packPath);
        Assert.Equal(0, next.ExitCode);
        Assert.Contains("- kind: verify\n", (await File.ReadAllTextAsync(packPath)).ReplaceLineEndings("\n"));

        var estimate = await CliProcess.RunAsync(repo.Root, "estimate");
        Assert.Equal(0, estimate.ExitCode);
        Assert.Contains("total ~", estimate.Stdout);
    }

    [Fact]
    public async Task Init_AppendsLedgerToGitignore_AndIsIdempotent()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var gitignore = Path.Combine(repo.Root, ".gitignore");
        await File.WriteAllTextAsync(gitignore, "bin/\nobj/");

        var first = await CliProcess.RunAsync(repo.Root, "init", "--yes");
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("added .codemuster/ledger.db to .gitignore", first.Stdout);
        Assert.Equal("bin/\nobj/\n.codemuster/ledger.db\n.codemuster/ledger.db-*\n", await File.ReadAllTextAsync(gitignore));
        var config = await File.ReadAllTextAsync(Path.Combine(repo.Root, ".codemuster", "config.json"));

        var second = await CliProcess.RunAsync(Path.Combine(repo.Root, "web"), "init", "--yes");
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("already present", second.Stdout);
        Assert.Contains("already ignores", second.Stdout);
        Assert.Equal("bin/\nobj/\n.codemuster/ledger.db\n.codemuster/ledger.db-*\n", await File.ReadAllTextAsync(gitignore));
        Assert.Equal(config, await File.ReadAllTextAsync(Path.Combine(repo.Root, ".codemuster", "config.json")));
    }

    [Theory]
    [InlineData("init --no-gitignore")]
    [InlineData("init --yes --no-gitignore")]
    public async Task Init_NoGitignore_LeavesGitignoreAlone(string arguments)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        File.Delete(Path.Combine(repo.Root, ".gitignore"));

        var init = await CliProcess.RunAsync(repo.Root, arguments.Split(' '));

        Assert.Equal(0, init.ExitCode);
        Assert.Contains("left .gitignore alone", init.Stdout);
        Assert.False(File.Exists(Path.Combine(repo.Root, ".gitignore")));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".codemuster", "config.json")));
    }

    [Fact]
    public async Task Init_TreatsALeadingSpacePatternAsNotIgnored()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var gitignore = Path.Combine(repo.Root, ".gitignore");
        await File.WriteAllTextAsync(gitignore, "  .codemuster/ledger.db\n");

        var init = await CliProcess.RunAsync(repo.Root, "init", "--yes");

        Assert.Equal(0, init.ExitCode);
        Assert.Contains("added .codemuster/ledger.db to .gitignore", init.Stdout);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        Assert.DoesNotContain("ledger.db", repo.Git("status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task NextToStdout_PrintsPack()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var next = await CliProcess.RunAsync(repo.Root, "next", "--batch", "2");

        Assert.Equal(0, next.ExitCode);
        Assert.Equal(2, Regex.Matches(next.Stdout, "^# CodeMuster unit$", RegexOptions.Multiline).Count);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("done")]
    [InlineData("next --batch")]
    [InlineData("init extra")]
    [InlineData("scan --yes")]
    [InlineData("scan --mode files")]
    [InlineData("scan --depth 2")]
    [InlineData("next --yes")]
    [InlineData("next --batch 2 --no-gitignore")]
    [InlineData("done u --fingerprint f --findings x --yes")]
    public async Task BadUsage_PrintsUsage_AndExits2(string arguments)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), arguments.Split(' '));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage: codemuster", result.Stderr);
    }
}
