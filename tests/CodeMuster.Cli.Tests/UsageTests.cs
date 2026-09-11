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
}
