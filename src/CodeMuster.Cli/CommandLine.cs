namespace CodeMuster.Cli;

public sealed record Command(string Verb, IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string> Options, IReadOnlySet<string> Flags);

public static class CommandLine
{
    private static readonly HashSet<string> FlagNames = ["yes", "no-gitignore", "global", "force", "include-refuted", "stash", "no-hooks", "no-skills", "retry-declined", "allow-failing-tests"];
    private static readonly Dictionary<string, string> ShortOptions = new(StringComparer.Ordinal) { ["-j"] = "jobs" };

    public static Command? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) && !ShortOptions.ContainsKey(args[i]))
            {
                positionals.Add(args[i]);
                continue;
            }

            var name = ShortOptions.TryGetValue(args[i], out var expanded) ? expanded : args[i][2..];
            if (FlagNames.Contains(name))
            {
                flags.Add(name);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || ShortOptions.ContainsKey(args[i + 1]))
            {
                return null;
            }

            options[name] = args[i + 1];
            i++;
        }

        return new Command(args[0].ToLowerInvariant(), positionals, options, flags);
    }
}
