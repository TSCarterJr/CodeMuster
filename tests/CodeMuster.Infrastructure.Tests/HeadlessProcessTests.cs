using System.Diagnostics;

namespace CodeMuster.Infrastructure.Tests;

public sealed class HeadlessProcessTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    [Fact]
    public async Task ChildProcessesSuppressProactivePluginHooksWithoutChangingTheParent()
    {
        var parent = Environment.GetEnvironmentVariable("CODEMUSTER_WORKER");
        var output = await HeadlessProcess.RunAsync("node", ["-e", "process.stdout.write(process.env.CODEMUSTER_WORKER || '')"], "", CancellationToken.None);

        Assert.Equal("1", output);
        Assert.Equal(parent, Environment.GetEnvironmentVariable("CODEMUSTER_WORKER"));
    }

    [Fact]
    public async Task ChildEnvironmentOverridesDoNotChangeTheParent()
    {
        var output = await HeadlessProcess.RunAsync("node", ["-e", "process.stdout.write(process.env.CODEMUSTER_TEST_POLICY)"], "", CancellationToken.None,
            environment: new Dictionary<string, string> { ["CODEMUSTER_TEST_POLICY"] = "restricted" });
        Assert.Equal("restricted", output);
        Assert.Null(Environment.GetEnvironmentVariable("CODEMUSTER_TEST_POLICY"));
    }

    [Fact]
    public async Task Runs_the_executable_with_an_argument_list_and_returns_stdout()
    {
        var shim = WriteShim("echo shim %~1\r\n", "echo shim $1\n");

        var output = await HeadlessProcess.RunAsync(shim, ["hello"], "", CancellationToken.None);

        Assert.Equal("shim hello", output.Trim());
    }

    [Fact]
    public async Task Writes_the_text_to_stdin()
    {
        var git = ExecutableResolver.Resolve("git");

        var output = await HeadlessProcess.RunAsync(git, ["hash-object", "--stdin"], "hello\n", CancellationToken.None);

        Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", output.Trim());
    }

    [Fact]
    public async Task UsesTheRequestedWorkingDirectory()
    {
        using var repo = new TempRepo();
        var output = await HeadlessProcess.RunAsync(ExecutableResolver.Resolve("git"), ["rev-parse", "--show-toplevel"], "", CancellationToken.None, repo.Root);
        Assert.Equal(repo.Run("rev-parse", "--show-toplevel").Trim().Replace('\\', '/'), output.Trim().Replace('\\', '/'));
    }

    [Fact]
    public async Task Non_zero_exit_throws_with_stderr()
    {
        var git = ExecutableResolver.Resolve("git");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => HeadlessProcess.RunAsync(git, ["--no-such-option"], "", CancellationToken.None));

        Assert.Contains("exited with code", ex.Message);
        Assert.Contains("no-such-option", ex.Message);
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        var shim = WriteShim("ping -n 60 127.0.0.1 > nul\r\n", "sleep 60\n");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HeadlessProcess.RunAsync(shim, [], "", cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
    }

    private string WriteShim(string windowsBody, string unixBody)
    {
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(_dir.Root, "shim.cmd");
            File.WriteAllText(cmd, "@echo off\r\n" + windowsBody);
            return cmd;
        }

        var script = Path.Combine(_dir.Root, "shim");
        File.WriteAllText(script, "#!/bin/sh\n" + unixBody);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return script;
    }

    public void Dispose() => _dir.Dispose();
}
