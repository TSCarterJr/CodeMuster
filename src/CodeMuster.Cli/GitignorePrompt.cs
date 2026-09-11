namespace CodeMuster.Cli;

public static class GitignorePrompt
{
    public static bool Decide(IReadOnlySet<string> flags, bool interactive, Func<string?> ask)
    {
        if (flags.Contains("no-gitignore"))
        {
            return false;
        }

        if (flags.Contains("yes") || !interactive)
        {
            return true;
        }

        var answer = ask()?.Trim();
        return answer is not null && (answer.Length == 0 || answer.StartsWith('y') || answer.StartsWith('Y'));
    }
}
