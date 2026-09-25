using System.Diagnostics;
using System.Text;

namespace CodeMuster.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

public static class CliProcess
{
    public static Task<CliResult> RunAsync(string workingDirectory, params string[] args) => RunAsync(workingDirectory, null, args);

    public static Task<CliResult> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args) =>
        StartAsync("dotnet", [Path.Combine(AppContext.BaseDirectory, "codemuster.dll"), .. args], workingDirectory, environment);

    /// <summary>
    /// Runs the executable the release ships (codemuster.exe or codemuster) instead of dotnet codemuster.dll. Its folder, not the
    /// dotnet install, is the first place Windows and .NET on Linux and macOS search for a program started by bare name.
    /// </summary>
    public static Task<CliResult> RunAppHostAsync(string workingDirectory, params string[] args)
    {
        // A framework-dependent test build finds its runtime through DOTNET_ROOT when dotnet is not in a default install location.
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return StartAsync(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "codemuster.exe" : "codemuster"), args, workingDirectory,
            new Dictionary<string, string> { ["DOTNET_ROOT"] = root });
    }

    private static async Task<CliResult> StartAsync(string program, IEnumerable<string> args, string workingDirectory, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // Claude Code's shell sets this, which hides a program in the working directory from Windows' search; a normal terminal does not.
        start.Environment.Remove("NoDefaultCurrentDirectoryInExePath");
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
