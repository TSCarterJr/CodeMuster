using System.Diagnostics;
using System.Text;

namespace CodeMuster.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

public static class CliProcess
{
    public static Task<CliResult> RunAsync(string workingDirectory, params string[] args) => RunAsync(workingDirectory, null, args);

    public static async Task<CliResult> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "codemuster.dll"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

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
