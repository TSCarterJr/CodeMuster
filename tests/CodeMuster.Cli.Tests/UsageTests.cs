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
}
