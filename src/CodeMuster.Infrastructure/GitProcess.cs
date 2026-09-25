using System.Diagnostics;
using System.Text;

namespace CodeMuster.Infrastructure;

internal static class GitProcess
{
    public static async Task<string> RunAsync(string repoRoot, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var (exitCode, output, error) = await RunAllowingFailureAsync(repoRoot, arguments, standardInput, cancellationToken, environment).ConfigureAwait(false);
        return exitCode == 0 ? output : throw Failure(arguments, exitCode, error);
    }

    public static InvalidOperationException Failure(IReadOnlyList<string> arguments, int exitCode, string error) =>
        new($"git {string.Join(' ', arguments)} exited with code {exitCode}: {error.Trim()}");

    public static async Task<(int ExitCode, string Output, string Error)> RunAllowingFailureAsync(string repoRoot, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo(ExecutableResolver.Resolve("git"))
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        if (standardInput is not null)
        {
            startInfo.StandardInputEncoding = utf8;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
