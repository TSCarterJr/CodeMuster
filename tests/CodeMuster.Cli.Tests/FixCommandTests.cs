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
        var before = repo.Git("status", "--porcelain");
        var head = repo.Git("rev-parse", "HEAD");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(1, fix.ExitCode);
        Assert.Contains("working tree has uncommitted changes", fix.Stderr);
        Assert.Contains("--stash", fix.Stderr);
        Assert.Equal(before, repo.Git("status", "--porcelain"));
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Empty(repo.Git("stash", "list"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task AFailingTestCommand_ThrowsTheFixAway_AndTheRunReportsIt(string jobs)
    {
        using var repo = await AuditedAsync();
        repo.WithTestCommand("git", "rev-parse", "--verify", "no-such-ref");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--attempts", "1", "-j", jobs);

        Assert.Equal(1, fix.ExitCode);
        Assert.Contains("running tests for", fix.Stdout);
        Assert.Contains("tests failed after fixing", fix.Stdout);
        Assert.Contains("fixed 0 finding(s)", fix.Stdout);
        Assert.Contains("gave up on", fix.Stderr);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("## Failed attempts", report.Stdout);
        Assert.Contains("test command failed:", report.Stdout);
        Assert.Contains("current status: failed", report.Stdout);
        repo.WithTestCommand("git", "--version");
        var retry = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "-j", jobs);
        Assert.Equal(0, retry.ExitCode);
        var recovered = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("test command failed:", recovered.Stdout);
        Assert.Contains("current status: done", recovered.Stdout);
    }

    [Fact]
    public async Task Fix_CommitsTrackedChanges_AndPreservesUntrackedFiles()
    {
        using var repo = await AuditedAsync();
        const string target = "src/MixedRepo.Api/Program.cs";
        var targetPath = Path.Combine(repo.Root, target);
        var original = await File.ReadAllTextAsync(targetPath);
        await File.WriteAllTextAsync(targetPath, original + "\n// fixture fix\n");
        var patch = repo.Git("diff", "--", target);
        await File.WriteAllTextAsync(targetPath, original);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "fix.patch"), patch);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "local notes.txt"), "keep my work\n");
        repo.Git("config", "user.name", "CodeMuster Tests");
        repo.Git("config", "user.email", "tests@codemuster.invalid");
        repo.Git("config", "commit.gpgsign", "false");
        repo.WithTestCommand("git", "apply", "fix.patch");
        var before = repo.Git("rev-parse", "HEAD").Trim();
        var untracked = repo.Git("ls-files", "--others", "--exclude-standard");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--path", target);

        Assert.Equal(0, fix.ExitCode);
        Assert.Equal("1", repo.Git("rev-list", "--count", before + "..HEAD").Trim());
        Assert.Equal(target, repo.Git("diff", "--name-only", before, "HEAD").Trim());
        Assert.Equal(untracked, repo.Git("ls-files", "--others", "--exclude-standard"));
        Assert.Equal("keep my work\n", await File.ReadAllTextAsync(Path.Combine(repo.Root, "local notes.txt")));
        Assert.Contains("// fixture fix", repo.Git("show", "HEAD:" + target));
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("  fix: fixed; ", report.Stdout);
    }

    [Fact]
    public async Task APassingTestCommand_LetsTheFixesThrough()
    {
        using var repo = await AuditedAsync();
        repo.WithTestCommand("git", "--version");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("running tests for", fix.Stdout);
        Assert.Matches(@"fixed [1-9]\d* finding\(s\)", fix.Stdout);
        Assert.DoesNotContain("no test_command", fix.Stderr);
    }

    [Fact]
    public async Task WithoutATestCommand_ItSaysNothingCheckedTheFix()
    {
        using var repo = await AuditedAsync();

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("no test_command", fix.Stderr);
    }

    [Fact]
    public async Task Fix_ParallelWorkers_RecordFindings_AndLeaveNoWorktreesBehind()
    {
        using var repo = await AuditedAsync();
        var worktrees = repo.Git("worktree", "list", "--porcelain");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "-j", "2");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("up to 2 files at a time", fix.Stdout);
        Assert.Matches(@"fixed [1-9]\d* finding\(s\) across [1-9]\d* file\(s\), declined 0, 0 gave up", fix.Stdout);
        Assert.Equal(worktrees, repo.Git("worktree", "list", "--porcelain"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public async Task Fix_RejectsInvalidParallelism(string jobs)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "-j", jobs);
        Assert.Equal(2, fix.ExitCode);
    }

    [Theory]
    [InlineData(false, "1")]
    [InlineData(true, "1")]
    [InlineData(false, "2")]
    [InlineData(true, "2")]
    public async Task Stash_RestoresLocalEditsAndIndex_AfterFixOrFailure(bool failTests, string jobs)
    {
        using var repo = await AuditedAsync();
        const string target = "src/MixedRepo.Api/Program.cs";
        repo.Git("config", "user.name", "CodeMuster Tests");
        repo.Git("config", "user.email", "tests@codemuster.invalid");
        repo.Git("config", "commit.gpgsign", "false");
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "memory.md"), "original\n");
        repo.Git("add", "memory.md");
        repo.Git("commit", "-qm", "memory fixture");
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "memory.md"), "staged\n");
        repo.Git("add", "memory.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "memory.md"), "unstaged\n");
        var targetPath = Path.Combine(repo.Root, target);
        var original = await File.ReadAllTextAsync(targetPath);
        await File.WriteAllTextAsync(targetPath, original + "\n// fixture fix\n");
        var patch = repo.Git("diff", "--", target);
        await File.WriteAllTextAsync(targetPath, original);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "fix.patch"), patch);
        repo.WithTestCommand(failTests ? ["git", "rev-parse", "--verify", "missing-ref"] : ["git", "apply", "fix.patch"]);
        var before = repo.Git("rev-parse", "HEAD").Trim();
        var staged = repo.Git("diff", "--cached");
        var unstaged = repo.Git("diff");
        var untracked = repo.Git("ls-files", "--others", "--exclude-standard");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--path", target, "--attempts", "1", "--stash", "-j", jobs);

        Assert.Equal(failTests ? 1 : 0, fix.ExitCode);
        Assert.Contains("restored your tracked changes", fix.Stdout);
        Assert.Equal(staged, repo.Git("diff", "--cached"));
        Assert.Equal(unstaged, repo.Git("diff"));
        Assert.Equal(untracked, repo.Git("ls-files", "--others", "--exclude-standard"));
        Assert.Equal(failTests ? "0" : "1", repo.Git("rev-list", "--count", before + "..HEAD").Trim());
        Assert.Equal(failTests ? "" : target, repo.Git("diff", "--name-only", before, "HEAD").Trim());
        Assert.Contains("codemuster fix", repo.Git("stash", "list"));
    }
}
