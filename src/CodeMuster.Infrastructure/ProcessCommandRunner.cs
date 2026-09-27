using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

/// <summary>Runs a program resolved on PATH with an argument list, never through a shell.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public bool IsOnPath(string executable)
    {
        try
        {
            ExecutableResolver.Resolve(executable);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<CommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        ProcessStartInfo startInfo;
        try
        {
            startInfo = new ProcessStartInfo(ExecutableResolver.Resolve(command[0]))
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(-1, "", ex.Message);
        }

        foreach (var argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{command[0]} did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new CommandResult(-1, "", ex.Message);
        }

        using (process)
        {
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new CommandResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
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
}
