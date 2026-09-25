namespace CodeMuster.Cli.Tests;

public class UsageTests
{
    [Fact]
    public async Task NoArguments_PrintsUsage_AndExits2()
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath());

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage: codemuster", result.Stderr);
    }

    [Fact]
    public async Task Version_PrintsTheBareVersion_OutsideARepository_AndExits0()
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Stderr);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", result.Stdout.TrimEnd());
    }

    [Fact]
    public async Task Usage_MentionsVersion()
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath());

        Assert.Contains("codemuster --version", result.Stderr);
    }
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task Help_PrintsOverviewSuccessfully_OutsideARepository(string argument)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.Contains("usage: codemuster", result.Stdout);
        Assert.Contains("codemuster help <command>", result.Stdout);
    }

    [Theory]
    [InlineData("init")]
    [InlineData("doctor")]
    [InlineData("scan")]
    [InlineData("status")]
    [InlineData("estimate")]
    [InlineData("next")]
    [InlineData("done")]
    [InlineData("run")]
    [InlineData("verify")]
    [InlineData("fix")]
    [InlineData("report")]
    [InlineData("skill")]
    [InlineData("update")]
    [InlineData("hook")]
    [InlineData("validate")]
    public async Task CommandHelp_WorksBeforeSetup_AndDoesNotExecuteTheCommand(string command)
    {
        foreach (var args in new[] { new[] { command, "--help" }, new[] { command, "-h" }, new[] { "help", command } })
        {
            var result = await CliProcess.RunAsync(Path.GetTempPath(), args);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
            Assert.StartsWith("usage: codemuster " + command, result.Stdout);
        }
    }

    [Fact]
    public async Task FixHelp_ExplainsLimitsRecoveryAndValidation_EvenWithOptions()
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), "fix", "--agent", "codex", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("up to N", result.Stdout);
        Assert.Contains("default: 1", result.Stdout);
        Assert.Contains("default: 3", result.Stdout);
        Assert.Contains("--stash", result.Stdout);
        Assert.Contains("test_command", result.Stdout);
        Assert.Contains("never pushes", result.Stdout);
    }

    [Theory]
    [InlineData("help", "unknown")]
    [InlineData("unknown", "--help")]
    public async Task UnknownCommandHelp_IsAUsageError(string first, string second)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), first, second);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Equal(["codemuster: unknown command \"unknown\"", "usage: codemuster <command> [options]; see codemuster --help"], Lines(result.Stderr));
    }

    [Theory]
    [InlineData("run", "codemuster run: --agent is required (claude, codex, gemini, opencode)", "usage: codemuster run --agent <name> [options]; see codemuster run --help")]
    [InlineData("run --agent claude -j 0", "codemuster run: -j must be a positive whole number (got \"0\")", "usage: codemuster run --agent <name> [options]; see codemuster run --help")]
    [InlineData("run --agent claude --bogus x", "codemuster run: unknown option --bogus (options: --agent, -j/--jobs, --attempts, --path, --model, --effort, --kind, --force)", "usage: codemuster run --agent <name> [options]; see codemuster run --help")]
    [InlineData("stauts", "codemuster: unknown command \"stauts\"; did you mean \"status\"?", "usage: codemuster <command> [options]; see codemuster --help")]
    [InlineData("next --pth web", "codemuster next: unknown option --pth; did you mean --path? (options: --batch, --out, --path, --kind)", "usage: codemuster next [--batch N] [--out <file>] [--path <path>] [--kind <kind>]; see codemuster next --help")]
    [InlineData("status --since yesterday", "codemuster status: unknown option --since (status takes no options)", "usage: codemuster status; see codemuster status --help")]
    public async Task ArgumentMistake_PrintsItsReasonAndTheCommandsUsageLine_AndExits2(string arguments, string reason, string usage)
    {
        var result = await CliProcess.RunAsync(Path.GetTempPath(), arguments.Split(' '));

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Equal([reason, usage], Lines(result.Stderr));
    }

    [Fact]
    public async Task RuntimeErrors_StartWithError_AndOptionsAcceptNameEqualsValue()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var gated = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Equal(2, gated.ExitCode);
        Assert.Equal(["error: not set up here; run `codemuster init`"], Lines(gated.Stderr));

        var init = await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        Assert.Equal(0, init.ExitCode);
        Assert.Contains("no agent skills selected", init.Stdout);

        var done = await CliProcess.RunAsync(repo.Root, "done", "u", "--fingerprint=f", "--findings=missing.json");
        Assert.Equal(2, done.ExitCode);
        var error = Assert.Single(Lines(done.Stderr));
        Assert.StartsWith("error: ", error);
        Assert.Contains("missing.json", error);
    }

    [Fact]
    public async Task FileArguments_ThatCannotBeReadOrWritten_NameTheOption_AndExit2()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);

        foreach (var (arguments, expected) in new (string[], string)[]
        {
            (["report", "--out", "nodir/x.md"], "error: --out nodir/x.md: folder nodir does not exist"),
            (["next", "--out", "nodir/p.md"], "error: --out nodir/p.md: folder nodir does not exist"),
            (["report", "--out", "web"], "error: --out web is a folder; name a file"),
            (["done", "u", "--fingerprint", "f", "--findings", "missing.json"], "error: --findings missing.json: no such file"),
            (["done", "u", "--fingerprint", "f", "--findings", "web"], "error: --findings web is a folder; name a file"),
        })
        {
            var result = await CliProcess.RunAsync(repo.Root, arguments);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal([expected], Lines(result.Stderr));
        }
    }

    [Fact]
    public async Task Outside_a_git_repository_the_error_says_so_in_plain_words()
    {
        var folder = Directory.CreateTempSubdirectory("codemuster-outside-");
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_CEILING_DIRECTORIES"] = folder.Parent!.FullName };

            var status = await CliProcess.RunAsync(folder.FullName, environment, "status");

            Assert.Equal(1, status.ExitCode);
            var error = Assert.Single(Lines(status.Stderr));
            Assert.StartsWith("error: ", error);
            Assert.EndsWith(" is not a git repository or inside one; run codemuster from a repository, or create one with git init", error);
            Assert.DoesNotContain("rev-parse", error);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Done_rejections_go_to_stderr_as_errors_and_a_malformed_file_is_located()
    {
        using var repo = TempRepo.FromFixture("minimal-api");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        var pack = (await CliProcess.RunAsync(repo.Root, "next")).Stdout;
        var unit = System.Text.RegularExpressions.Regex.Match(pack, @"^- unit: (\S+)\r?$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
        var fingerprint = System.Text.RegularExpressions.Regex.Match(pack, @"^- fingerprint: ([0-9a-f]{64})\r?$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
        File.WriteAllText(Path.Combine(repo.Root, "bad.json"), "not json\n");

        var stale = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", new string('0', 64), "--findings", "bad.json");
        var malformed = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", "bad.json");

        Assert.Equal(1, stale.ExitCode);
        Assert.Equal("", stale.Stdout);
        Assert.Equal([$"error: unit {unit} changed since next; run next again"], Lines(stale.Stderr));
        Assert.Equal(1, malformed.ExitCode);
        Assert.Equal("", malformed.Stdout);
        Assert.Equal(["error: --findings bad.json is not valid JSON (line 1, column 2); correct it and run the same done command"], Lines(malformed.Stderr));
    }

    private static string[] Lines(string output) => output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
