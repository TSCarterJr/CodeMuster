using System.Diagnostics;
using System.Text;

namespace CodeMuster.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

public static class CliProcess
{
    public static Task<CliResult> RunAsync(string workingDirectory, params string[] args) => RunAsync(workingDirectory, null, args);

    public static Task<CliResult> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args) =>
        StartAsync("dotnet", [Path.Combine(AppContext.BaseDirectory, "codemuster.dll"), .. args], workingDirectory, environment);

    /// <summary>Runs the CLI with <paramref name="input"/> on standard input, which is closed after it, as an MCP client does at shutdown.</summary>
    public static Task<CliResult> RunWithInputAsync(string workingDirectory, string input, params string[] args) =>
        StartAsync("dotnet", [Path.Combine(AppContext.BaseDirectory, "codemuster.dll"), .. args], workingDirectory, null, input);

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

    /// <summary>
    /// Starts the CLI and returns its running process (the dotnet host is the codemuster process) with output drained in the
    /// background. Its standard input is a pipe held open and never written, as an MCP client or an agent's shell holds it.
    /// </summary>
    public static Process Start(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in (string[])[Path.Combine(AppContext.BaseDirectory, "codemuster.dll"), .. args])
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["CODEMUSTER_TEST_AGENT"] = "1";
        // Remembered choices (D86) go to a scratch folder, never the developer's own ~/.codemuster.
        start.Environment["CODEMUSTER_STATE_DIR"] = Path.Combine(Path.GetTempPath(), "codemuster-cli-tests-state");
        var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static async Task<CliResult> StartAsync(string program, IEnumerable<string> args, string workingDirectory, IReadOnlyDictionary<string, string>? environment, string? input = null)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = input is not null,
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
        start.Environment["CODEMUSTER_TEST_AGENT"] = "1";
        // Remembered choices (D86) go to a scratch folder, never the developer's own ~/.codemuster.
        start.Environment["CODEMUSTER_STATE_DIR"] = Path.Combine(Path.GetTempPath(), "codemuster-cli-tests-state");
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await using var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            await stdin.WriteAsync(input);
        }

        await process.WaitForExitAsync();
        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
