namespace CodeMuster.Infrastructure.Tests;

public sealed class ProcessCommandRunnerTests : IDisposable
{
    private readonly TempDirectory _folder = new();
    private readonly ProcessCommandRunner _runner = new();

    [Fact]
    public async Task Run_PassesArgumentsAsAList_InTheWorkingDirectory()
    {
        var result = await _runner.RunAsync(_folder.Root, ["git", "init", "-q", "a folder with spaces"], CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(Path.Combine(_folder.Root, "a folder with spaces", ".git")));
    }

    [Fact]
    public async Task Run_ReportsAFailingExitCode_AndStandardError()
    {
        var result = await _runner.RunAsync(_folder.Root, ["git", "no-such-subcommand"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no-such-subcommand", result.Error);
    }

    [Fact]
    public async Task Run_AProgramNotOnPath_ReturnsWhyInsteadOfThrowing()
    {
        var result = await _runner.RunAsync(_folder.Root, ["codemuster-no-such-program"], CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("was not found on PATH", result.Error);
    }

    [Fact]
    public void IsOnPath_TellsWhetherAProgramCanBeFound()
    {
        Assert.True(_runner.IsOnPath("git"));
        Assert.False(_runner.IsOnPath("codemuster-no-such-program"));
    }

    public void Dispose()
    {
        foreach (var file in new DirectoryInfo(_folder.Root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        _folder.Dispose();
    }
}
