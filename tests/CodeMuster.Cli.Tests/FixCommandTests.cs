using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class FixCommandTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("next")]
    public async Task AnotherCoordinatorIsRefusedWithoutTouchingTheLedger(string verb)
    {
        using var repo = await AuditedAsync();
        using var held = new FileStream(Path.Combine(repo.Root, ".git", "codemuster-coordinator.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var result = await CliProcess.RunAsync(repo.Root, verb);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("another CodeMuster command", result.Stderr);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task ValidationCannotCommitOutsideScope(string jobs)
    {
        using var repo = await AuditedAsync();
        const string target = "src/MixedRepo.Api/Program.cs";
        const string other = "web/lib/api.ts";
        await File.AppendAllTextAsync(Path.Combine(repo.Root, other), "\n// validation edit\n");
        var patch = repo.Git("diff", "--", other);
        repo.Git("restore", "--", other);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "scope.patch"), patch);
        repo.WithTestCommand("git", "apply", "scope.patch");
        var head = repo.Git("rev-parse", "HEAD");
        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--path", target, "-j", jobs, "--attempts", "1");
        Assert.Equal(1, fix.ExitCode);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Contains(other, fix.Stdout + fix.Stderr);
        Assert.DoesNotContain("fix: fixed", (await CliProcess.RunAsync(repo.Root, "report")).Stdout);
        Assert.Contains("validation edit", await File.ReadAllTextAsync(Path.Combine(repo.Root, other)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task RejectedCommitKeepsFindingsUnfixedAndPreservesRecovery(string jobs)
    {
        using var repo = await AuditedAsync();
        const string target = "src/MixedRepo.Api/Program.cs";
        var hook = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nexit 1\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var head = repo.Git("rev-parse", "HEAD");
        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--path", target, "-j", jobs, "--attempts", "1");
        Assert.Equal(1, fix.ExitCode);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.DoesNotContain("fix: fixed", (await CliProcess.RunAsync(repo.Root, "report")).Stdout);
        Assert.Contains("codemuster fake fix", await File.ReadAllTextAsync(Path.Combine(repo.Root, target)));
        Assert.Contains("preserved", fix.Stdout + fix.Stderr);
    }

    private static async Task<TempRepo> AuditedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        repo.Git("config", "user.name", "CodeMuster Tests");
        repo.Git("config", "user.email", "tests@codemuster.invalid");
        repo.Git("config", "commit.gpgsign", "false");
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
    public async Task Validation_RunsAfterTheQueueIsDone_AndMissingConfigurationIsNotAPass()
    {
        using var repo = await AuditedAsync();
        Assert.NotEqual(0, (await CliProcess.RunAsync(repo.Root, "validate")).ExitCode);
        repo.WithTestCommand("git", "--version");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "validate")).ExitCode);
        repo.WithTestCommand("git", "rev-parse", "--verify", "missing-ref");
        Assert.Equal(1, (await CliProcess.RunAsync(repo.Root, "validate")).ExitCode);
    }

    [Fact]
    public async Task Fix_ExplicitRecoveryOptions_AreAccepted()
    {
        using var repo = await AuditedAsync();
        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--retry-declined", "--path", "src/MixedRepo.Api/Program.cs", "--include-related", "web/lib/api.ts");
        Assert.Equal(0, fix.ExitCode);
    }

    [Fact]
    public async Task Fix_RecordsAnOutcomeForEveryConfirmedFinding()
    {
        using var repo = await AuditedAsync();
        var before = repo.Git("rev-parse", "HEAD").Trim();

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("fixing with fake, one file at a time", fix.Stdout);
        var summary = Assert.Single(System.Text.RegularExpressions.Regex.Matches(fix.Stdout, @"fixed [1-9]\d* finding\(s\) across ([1-9]\d*) file\(s\), declined 0, 0 gave up"));
        Assert.Equal(summary.Groups[1].Value, repo.Git("rev-list", "--count", before + "..HEAD").Trim());
        Assert.Empty(repo.Git("status", "--porcelain", "--untracked-files=no"));

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
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "local notes.txt"), "keep my work\n");
        repo.WithTestCommand("git", "--version");
        var before = repo.Git("rev-parse", "HEAD").Trim();
        var untracked = repo.Git("ls-files", "--others", "--exclude-standard");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "--path", target);

        Assert.Equal(0, fix.ExitCode);
        Assert.Equal("1", repo.Git("rev-list", "--count", before + "..HEAD").Trim());
        Assert.Equal(target, repo.Git("diff", "--name-only", before, "HEAD").Trim());
        Assert.Equal(untracked, repo.Git("ls-files", "--others", "--exclude-standard"));
        Assert.Equal("keep my work\n", await File.ReadAllTextAsync(Path.Combine(repo.Root, "local notes.txt")));
        Assert.Contains("// codemuster fake fix", repo.Git("show", "HEAD:" + target));
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
    public async Task AnOversizedFile_IsSkipped_TheRestAreFixed_AndARaisedBudgetPicksItUp()
    {
        using var repo = await AuditedAsync();
        const string target = "src/MixedRepo.Api/Program.cs";
        repo.Git("config", "user.name", "CodeMuster Tests");
        repo.Git("config", "user.email", "tests@codemuster.invalid");
        repo.Git("config", "commit.gpgsign", "false");
        await File.AppendAllTextAsync(Path.Combine(repo.Root, target), new string('x', 120_000));
        repo.Git("commit", "-am", "grow the endpoint file");

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "-j", "2");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains(target + " exceeds the whole-file pack limit", fix.Stdout);
        Assert.Matches(@"fixed [1-9]\d* finding\(s\) across [1-9]\d* file\(s\), declined 0, 1 skipped, 0 gave up", fix.Stdout);
        Assert.Contains("| " + target + " | skipped |", (await CliProcess.RunAsync(repo.Root, "report")).Stdout);

        repo.WithSliceTokenBudget(60_000);
        var again = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake");

        Assert.Equal(0, again.ExitCode);
        Assert.DoesNotContain("skipped", again.Stdout);
        Assert.Matches(@"fixed [1-9]\d* finding\(s\) across 1 file\(s\), declined 0, 0 gave up", again.Stdout);
    }

    [Fact]
    public async Task Fix_ParallelWorkers_RecordFindings_AndLeaveNoWorktreesBehind()
    {
        using var repo = await AuditedAsync();
        var worktrees = Worktrees(repo);
        var before = repo.Git("rev-parse", "HEAD").Trim();

        var fix = await CliProcess.RunAsync(repo.Root, "fix", "--agent", "fake", "-j", "2");

        Assert.Equal(0, fix.ExitCode);
        Assert.Contains("up to 2 files at a time", fix.Stdout);
        var summary = Assert.Single(System.Text.RegularExpressions.Regex.Matches(fix.Stdout, @"fixed [1-9]\d* finding\(s\) across ([1-9]\d*) file\(s\), declined 0, 0 gave up"));
        Assert.Equal(summary.Groups[1].Value, repo.Git("rev-list", "--count", before + "..HEAD").Trim());
        Assert.Equal(worktrees, Worktrees(repo));
    }

    private static string[] Worktrees(TempRepo repo) =>
        repo.Git("worktree", "list", "--porcelain").Split('\n').Where(line => line.StartsWith("worktree ", StringComparison.Ordinal)).ToArray();

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
        repo.WithTestCommand(failTests ? ["git", "rev-parse", "--verify", "missing-ref"] : ["git", "--version"]);
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
