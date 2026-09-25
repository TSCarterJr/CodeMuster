namespace CodeMuster.Infrastructure.Tests;

public class CommandTestRunnerTests
{
    [Fact]
    public async Task Standard_output_and_standard_error_are_kept_apart()
    {
        using var repo = new TempRepo();

        var passed = await new CommandTestRunner(repo.Root, ["git", "--version"]).RunAsync(CancellationToken.None);
        var failed = await new CommandTestRunner(repo.Root, ["git", "codemuster-no-such-subcommand"]).RunAsync(CancellationToken.None);

        Assert.True(passed.Passed);
        Assert.StartsWith("git version ", passed.Output);
        Assert.Equal("", passed.Error);
        Assert.False(failed.Passed);
        Assert.Equal("", failed.Output);
        Assert.Contains("codemuster-no-such-subcommand", failed.Error);
    }
}
