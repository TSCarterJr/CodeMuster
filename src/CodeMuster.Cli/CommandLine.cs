namespace CodeMuster.Cli;

public sealed record Command(string Verb, IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string> Options);

public static class CommandLine
{
    public static Command? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(args[i]);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            options[args[i][2..]] = args[i + 1];
            i++;
        }

        return new Command(args[0].ToLowerInvariant(), positionals, options);
    }
}
