using System.Diagnostics;
using System.Text;

namespace CodeMuster.Infrastructure;

internal static class HeadlessProcess
{
    public static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{executable} did not start.");
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
            return process.ExitCode == 0
                ? output
                : throw new InvalidOperationException($"{Path.GetFileName(executable)} {string.Join(' ', arguments)} exited with code {process.ExitCode}: {error.Trim()}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
