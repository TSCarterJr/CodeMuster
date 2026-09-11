using System.Diagnostics;

namespace CodeMuster.Cli.Tests;

public sealed class TempRepo : IDisposable
{
    private static readonly string[] Skipped = ["bin", "obj", "node_modules"];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "codemuster-e2e-" + Guid.NewGuid().ToString("N"));

    public static TempRepo FromFixture(string name)
    {
        var repo = new TempRepo();
        Copy(Path.Combine(FindRepoRoot(), "fixtures", name), repo.Root);
        repo.Git("init", "-q");
        repo.Git("config", "core.autocrlf", "false");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "fixture");
        return repo;
    }

    public string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? stdout : throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMuster.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CodeMuster.sln not found above " + AppContext.BaseDirectory);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (!Skipped.Contains(name))
            {
                Copy(directory, Path.Combine(target, name));
            }
        }
    }
}
