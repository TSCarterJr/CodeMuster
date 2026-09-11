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
        foreach (var directory in pathDirectories)
        {
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException($"'{name}' was not found on PATH. Install it with: {InstallHint(name)}");
    }

    private static string InstallHint(string name) => name switch
    {
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "codex" => "npm install -g @openai/codex",
        "gemini" => "npm install -g @google/gemini-cli",
        "opencode" => "npm install -g opencode-ai",
        _ => $"install {name} and add it to PATH",
    };
}
