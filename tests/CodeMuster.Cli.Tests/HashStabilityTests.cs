namespace CodeMuster.Cli.Tests;

public class HashStabilityTests
{
    [Fact]
    public async Task Committing_analyzed_crlf_content_unchanged_under_autocrlf_leaves_no_stale_unit()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.Git("config", "core.autocrlf", "true");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake")).ExitCode);
        var path = Path.Combine(repo.Root, "web", "lib", "index.ts");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\r\n") + "export const extra = 1;\r\n");
        Assert.Contains(": 0 new, 1 stale,", (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).Stdout);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake")).ExitCode);
        repo.Git("add", "web/lib/index.ts");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "commit the analyzed content");

        var scan = await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        Assert.Contains(": 0 new, 0 stale,", scan.Stdout);
        Assert.EndsWith("\ncomplete", (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n").TrimEnd());
    }
}
