using System.Globalization;
using CodeMuster.Application;
using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Cli;

public sealed record Command(string Verb, IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string> Options, IReadOnlySet<string> Flags);

public sealed class UsageException(string message, string usage) : Exception(message)
{
    public string Usage { get; } = usage;
}

public static class CommandLine
{
    // Positional is a placeholder such as <unit> that takes any value, or a literal subcommand such as install.
    private sealed record Spec(string[] Options, string[] Flags, string[]? Required = null, string? Positional = null);

    private static readonly Dictionary<string, Spec> Verbs = new(StringComparer.Ordinal)
    {
        ["init"] = new(["for"], ["yes", "no-gitignore", "no-hooks", "no-skills"]),
        ["intelligent-config"] = new(["agent", "model", "effort"], []),
        ["doctor"] = new([], []),
        ["scan"] = new(["mode"], []),
        ["status"] = new([], []),
        ["estimate"] = new(["path"], []),
        ["next"] = new(["batch", "out", "path", "kind"], []),
        ["done"] = new(["fingerprint", "findings"], [], ["fingerprint", "findings"], "<unit>"),
        ["run"] = new(["agent", "jobs", "attempts", "path", "model", "effort", "kind"], ["force"], ["agent"]),
        ["verify"] = new(["agent", "jobs", "attempts", "path", "model", "effort"], ["force"], ["agent"]),
        ["report"] = new(["out"], ["include-refuted"]),
        ["skill"] = new(["for"], ["global"], ["for"], "install"),
        ["fix"] = new(["agent", "jobs", "attempts", "path", "model", "effort", "include-related"], ["stash", "retry-declined", "allow-failing-tests"], ["agent"]),
        ["hook"] = new([], []),
        ["validate"] = new([], []),
    };

    private static readonly string[] Kinds = Enum.GetNames<UnitKind>().Select(name => name.ToLowerInvariant()).Except(["fix", "dependency", "deadcode"]).ToArray();

    public static Command Parse(string[] args)
    {
        var verb = args.Length == 0 ? "" : args[0].ToLowerInvariant();
        if (!Verbs.TryGetValue(verb, out var spec))
        {
            throw UnknownCommand(args);
        }

        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var typed = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!IsOption(args[i]))
            {
                positionals.Add(args[i]);
                continue;
            }

            var equals = args[i].IndexOf('=', StringComparison.Ordinal);
            var spelled = equals < 0 ? args[i] : args[i][..equals];
            var name = spelled == "-j" ? "jobs" : spelled[2..];
            if (spec.Flags.Contains(name))
            {
                if (equals >= 0) throw Mistake(verb, $"{spelled} takes no value");
                flags.Add(name);
                continue;
            }

            if (!spec.Options.Contains(name))
            {
                throw Mistake(verb, UnknownOption(verb, spelled, name, spec));
            }

            var value = equals >= 0 ? args[i][(equals + 1)..] : i + 1 < args.Length && !IsOption(args[i + 1]) ? args[++i] : "";
            if (value.Length == 0) throw Mistake(verb, $"{spelled} needs a value");
            options[name] = value;
            typed[name] = spelled;
        }

        CheckPositionals(verb, spec.Positional, positionals);
        if (verb == "done" && positionals[0].StartsWith(UnitIds.Fix(""), StringComparison.Ordinal))
        {
            // done cannot see whether the file changed, so a hand-typed fix unit would record Fixed over untouched code.
            throw Mistake(verb, $"fix units are recorded by codemuster fix, which applies, tests and commits the repair; run codemuster fix --agent <agent> --path {positionals[0][UnitIds.Fix("").Length..]}");
        }

        foreach (var required in spec.Required ?? [])
        {
            var choices = required == "agent" ? AgentAdapters.Names : Choices(verb, required);
            if (!options.ContainsKey(required)) throw Mistake(verb, $"--{required} is required" + (choices is null ? "" : $" ({string.Join(", ", choices)})"));
        }

        foreach (var name in spec.Options)
        {
            if (options.TryGetValue(name, out var value) && ValueMistake(verb, name, value) is { } problem) throw Mistake(verb, $"{typed[name]} {problem}");
        }

        if (flags.Contains("no-skills") && options.ContainsKey("for")) throw Mistake(verb, "--for cannot be used with --no-skills");
        return new Command(verb, positionals, options, flags);
    }

    private static bool IsOption(string arg) => arg.StartsWith("--", StringComparison.Ordinal) || arg == "-j";

    private static void CheckPositionals(string verb, string? expected, List<string> positionals)
    {
        if (expected is null)
        {
            if (positionals.Count > 0) throw Mistake(verb, $"unexpected argument \"{positionals[0]}\"");
            return;
        }

        if (positionals.Count == 0) throw Mistake(verb, expected.StartsWith('<') ? $"missing {expected}" : $"expected \"{expected}\"");
        if (!expected.StartsWith('<') && positionals[0] != expected) throw Mistake(verb, $"expected \"{expected}\" (got \"{positionals[0]}\")");
        if (positionals.Count > 1) throw Mistake(verb, $"unexpected argument \"{positionals[1]}\"");
    }

    private static string? ValueMistake(string verb, string name, string value)
    {
        if (name is "jobs" or "attempts" or "batch")
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? null : $"must be a positive whole number (got \"{value}\")";
        }

        return Choices(verb, name) is { } choices && !choices.Contains(value) ? $"must be one of {string.Join(", ", choices)} (got \"{value}\")" : null;
    }

    private static IReadOnlyList<string>? Choices(string verb, string name) => (verb, name) switch
    {
        ("skill", "for") => SkillInstaller.Harnesses,
        (_, "kind") => Kinds,
        ("scan", "mode") => ["slice", "file"],
        _ => null,
    };

    private static string UnknownOption(string verb, string spelled, string name, Spec spec)
    {
        string[] names = [.. spec.Options, .. spec.Flags];
        if (names.Length == 0) return $"unknown option {spelled} ({verb} takes no options)";
        var suggestion = Nearest(name, names) is { } near ? $"; did you mean --{near}?" : "";
        return $"unknown option {spelled}{suggestion} (options: {string.Join(", ", names.Select(n => n == "jobs" ? "-j/--jobs" : "--" + n))})";
    }

    private static UsageException UnknownCommand(string[] args)
    {
        if (args.Length == 0) return new UsageException("codemuster: no command given", HelpText.UsageLine(null));
        var verb = args[0].ToLowerInvariant();
        if (verb == "update") return new UsageException("codemuster update: only the npm launcher can update CodeMuster; install it with npm i -g codemuster", HelpText.UsageLine("update"));
        if (verb == "help" && args.Length != 2) return new UsageException("codemuster help: name one command", "usage: codemuster help <command>; see codemuster --help");
        var named = verb == "help" ? args[1] : args[0];
        var suggestion = Nearest(named.ToLowerInvariant(), [.. Verbs.Keys, "update"]);
        return new UsageException($"codemuster: unknown command \"{named}\"" + (suggestion is null ? "" : $"; did you mean \"{suggestion}\"?"), HelpText.UsageLine(null));
    }

    private static UsageException Mistake(string verb, string problem) => new($"codemuster {verb}: {problem}", HelpText.UsageLine(verb));

    private static string? Nearest(string typed, IEnumerable<string> names)
    {
        string? best = null;
        var bestDistance = Math.Max(1, typed.Length / 3) + 1;
        foreach (var name in names)
        {
            var distance = Distance(typed, name);
            if (distance < bestDistance)
            {
                best = name;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Edit distance that counts swapping two neighbouring letters as one edit, so "stauts" is one edit from "status".
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j], d[i, j - 1]) + 1, d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[a.Length, b.Length];
    }
}
