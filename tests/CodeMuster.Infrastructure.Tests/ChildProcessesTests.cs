using System.Diagnostics;

namespace CodeMuster.Infrastructure.Tests;

/// <summary>PROC1 (D77): every launcher starts its child through <see cref="ChildProcesses"/>.</summary>
public class ChildProcessesTests
{
    private const string StdinReader =
        "let bytes = 0; process.stdin.on('data', (d) => { bytes += d.length; }); process.stdin.on('end', () => { process.stdout.write('stdin bytes ' + bytes); });";

    [Fact]
    public async Task A_child_given_no_input_reads_end_of_input_at_once()
    {
        var start = new ProcessStartInfo(ExecutableResolver.Resolve("node")) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(StdinReader);

        using var process = ChildProcesses.Start(start);
        var output = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync();

        Assert.Equal("stdin bytes 0", output);
    }

    [Fact]
    public async Task A_launcher_that_writes_input_still_can()
    {
        var start = new ProcessStartInfo(ExecutableResolver.Resolve("node")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(StdinReader);

        using var process = ChildProcesses.Start(start);
        await process.StandardInput.WriteAsync("abc");
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("stdin bytes 3", output);
    }

    [Fact]
    public void A_program_that_cannot_start_still_reports_why()
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), "codemuster-no-such-program-" + Guid.NewGuid().ToString("N"))) { UseShellExecute = false };

        Assert.Throws<System.ComponentModel.Win32Exception>(() => ChildProcesses.Start(start));
    }

    [Fact]
    public void Every_launcher_starts_its_child_through_ChildProcesses()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var direct = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => Path.GetFileName(path) != "ChildProcesses.cs" && File.ReadAllText(path).Contains("Process.Start(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))
            .ToList();

        Assert.Empty(direct);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMuster.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CodeMuster.sln not found above " + AppContext.BaseDirectory);
    }
}
