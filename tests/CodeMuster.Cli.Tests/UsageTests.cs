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
        Assert.Contains("usage: codemuster", result.Stderr);
    }
}
