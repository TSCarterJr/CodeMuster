using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class FixCommandTests
{
    private static async Task<TempRepo> AuditedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        var template = Path.Combine(repo.Root, "template.json");
        var planted = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample).Findings[0] with { Confidence = 0.9 };
        await File.WriteAllTextAsync(template, AnalysisResponseJson.Serialize(new AnalysisResponse("fake summary", [planted])));
        var environment = new Dictionary<string, string> { ["CODEMUSTER_FAKE_RESPONSE"] = template };
        await CliProcess.RunAsync(repo.Root, environment, "run", "--kind", "file", "--agent", "fake", "-j", "4");
        await CliProcess.RunAsync(repo.Root, environment, "verify", "--agent", "fake", "-j", "4");
        File.Delete(template);
        return repo;
    }

    [Fact]
    public async Task Fix_RecordsAnOutcomeForEveryConfirmedFinding()
    {
        using var repo = await AuditedAsync();

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("fixing with fake, one file at a time", fix.Stdout);
        Assert.Matches(@"fixed [1-9]\d* finding\(s\) across [1-9]\d* file\(s\), declined 0, 0 gave up", fix.Stdout);

        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.Matches(@"(?m)^fix (\d+)/\1$", status);
    }

    [Fact]
    public async Task Fix_RefusesToStartWithUncommittedChanges()
    {
        using var repo = await AuditedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "src", "MixedRepo.Api", "Program.cs"), "// edited by hand\n");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(1, fix.ExitCode);
        Assert.Contains("working tree has uncommitted changes", fix.Stderr);
    }
}
