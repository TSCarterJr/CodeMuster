using System.Diagnostics;

namespace CodeMuster.Cli.Tests;

/// <summary>PROC1 (D77): a child codemuster starts never outlives it and never reads its standard input.</summary>
public class ChildProcessLifetimeTests
{
    private const string SleepingChild = "require('fs').writeFileSync(process.argv[2], String(process.pid)); setTimeout(() => {}, 60000);\n";

    private const string StdinReader =
        "let bytes = 0; process.stdin.on('data', (d) => { bytes += d.length; }); process.stdin.on('end', () => { console.log('stdin bytes ' + bytes); process.exit(0); });\n";

    [Theory]
    [InlineData("TERM")]
    [InlineData("HUP")]
    public async Task A_killed_codemuster_takes_its_test_command_child_with_it(string signal)
    {
        using var repo = await InitializedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "child.js"), SleepingChild);
        repo.WithTestCommand("node", "child.js", "child.pid");
        var pidFile = Path.Combine(repo.Root, "child.pid");

        using var codemuster = CliProcess.Start(repo.Root, "validate");
        int? child = null;
        try
        {
            child = await WaitForPidAsync(pidFile, codemuster);

            // The hard case: Windows gets no chance to clean up; macOS and Linux get an agent timeout's SIGTERM or a closed terminal's SIGHUP.
            if (OperatingSystem.IsWindows())
            {
                codemuster.Kill();
            }
            else
            {
                using var kill = Process.Start("kill", ["-" + signal, codemuster.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                await kill.WaitForExitAsync();
            }

            await codemuster.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (IsRunning(child.Value) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            Assert.False(IsRunning(child.Value), $"test_command child {child} outlived the killed codemuster process {codemuster.Id}");
        }
        finally
        {
            Kill(codemuster);
            if (child is { } pid)
            {
                KillPid(pid);
            }
        }
    }

    [Fact]
    public async Task A_test_command_reading_standard_input_gets_end_of_input_instead_of_codemusters_input()
    {
        using var repo = await InitializedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "stdin.js"), StdinReader);
        repo.WithTestCommand("node", "stdin.js");

        using var codemuster = CliProcess.Start(repo.Root, "validate");
        try
        {
            await Task.WhenAny(codemuster.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.True(codemuster.HasExited, "validate waited on a test_command reading codemuster's standard input");
            Assert.Equal(0, codemuster.ExitCode);
        }
        finally
        {
            Kill(codemuster);
        }
    }

    private static async Task<TempRepo> InitializedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes")).ExitCode);
        repo.WithoutVulnerabilityScan();
        return repo;
    }

    private static async Task<int> WaitForPidAsync(string pidFile, Process codemuster)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) && int.TryParse(await File.ReadAllTextAsync(pidFile), out var pid))
            {
                return pid;
            }

            Assert.False(codemuster.HasExited, "codemuster exited before its test_command started");
            await Task.Delay(100);
        }

        throw new TimeoutException("the test_command child never wrote its pid");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void KillPid(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Kill(process);
        }
        catch (ArgumentException)
        {
        }
    }
}
