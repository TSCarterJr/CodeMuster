namespace CodeMuster.Infrastructure;

public static class ExecutableResolver
{
    private static readonly string[] DefaultWindowsExtensions = [".exe", ".cmd", ".bat", ".com"];

    public static string Resolve(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? DefaultWindowsExtensions
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Resolve(name, directories, extensions, OperatingSystem.IsWindows());
    }

    public static string Resolve(string name, IEnumerable<string> pathDirectories, IEnumerable<string> pathExtensions, bool isWindows)
    {
        List<string> fileNames = isWindows ? [.. pathExtensions.Select(extension => name + extension)] : [name];
        // A relative entry such as "." would resolve against the current directory, which is usually the repository being audited.
        foreach (var directory in pathDirectories.Select(directory => directory.Trim('"')).Where(Path.IsPathFullyQualified))
        {
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate) && IsExecutable(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException($"'{name}' was not found on PATH. {InstallHint(name)}");
    }

    private static bool IsExecutable(string path) =>
        OperatingSystem.IsWindows()
        || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    private static string InstallHint(string name) => name switch
    {
        "claude" => "Install it with: npm install -g @anthropic-ai/claude-code",
        "codex" => "Install it with: npm install -g @openai/codex",
        "gemini" => "Install it with: npm install -g @google/gemini-cli",
        "opencode" => "Install it with: npm install -g opencode-ai",
        "git" => "Install Git from https://git-scm.com/downloads and add it to PATH.",
        "node" => "Install Node.js 22 or later from https://nodejs.org and add it to PATH.",
        _ => $"Install {name} and add it to PATH.",
    };
}
