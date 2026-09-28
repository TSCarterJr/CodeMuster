using System.Diagnostics;
using System.Text;

namespace CodeMuster.Infrastructure;

/// <summary>What a finished harness process printed, kept whatever its exit code, since a failed agent call can still report the usage it paid for.</summary>
internal sealed record ProcessOutput(int ExitCode, string Output, string Error)
{
    public string Failure(string executable, IReadOnlyList<string> arguments) =>
        $"{Path.GetFileName(executable)} {string.Join(' ', arguments)} exited with code {ExitCode}: {Error.Trim()}";
}

internal static class HeadlessProcess
{
    public static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await CaptureAsync(executable, arguments, standardInput, cancellationToken, workingDirectory, environment).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output : throw new InvalidOperationException(result.Failure(executable, arguments));
    }

    public static async Task<ProcessOutput> CaptureAsync(string executable, IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) startInfo.Environment[key] = value;
        startInfo.Environment["CODEMUSTER_WORKER"] = "1";

        using var process = ChildProcesses.Start(startInfo);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            return new ProcessOutput(process.ExitCode, output, error);
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
