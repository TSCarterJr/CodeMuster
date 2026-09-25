using System.Diagnostics;
using System.Text;

namespace CodeMuster.Infrastructure.Tests;

public sealed class TempRepo : IDisposable
{
    public TempRepo(string? objectFormat = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Run(objectFormat is null ? ["init", "-q"] : ["init", "-q", "--object-format=" + objectFormat]);
        Run("config", "core.autocrlf", "false");
        Run("config", "user.name", "CodeMuster Tests");
        Run("config", "user.email", "tests@codemuster.invalid");
    }

    public string Root { get; }

    public string Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Result}");
        }

        return stdout.Result;
    }

    public string Commit(string message)
    {
        Run("add", "-A");
        Run("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
        return Run("rev-parse", "HEAD").Trim();
    }

    public void WriteFile(string relativePath, string content) =>
        WriteBytes(relativePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    public void Dispose()
    {
        var dir = new DirectoryInfo(Root);
        if (!dir.Exists)
        {
            return;
        }

        foreach (var info in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            info.Attributes = FileAttributes.Normal;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                dir.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }
}
