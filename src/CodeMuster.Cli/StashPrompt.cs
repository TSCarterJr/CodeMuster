namespace CodeMuster.Cli;

public static class StashPrompt
{
    public static bool Decide(IReadOnlySet<string> flags, bool interactive, Func<string?> ask)
    {
        if (flags.Contains("stash"))
        {
            return true;
        }

        if (!interactive)
        {
            return false;
        }

        var answer = ask()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
