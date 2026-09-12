using System.Globalization;
using System.Reflection;
using System.Text;
using CodeMuster.Application;
using CodeMuster.Domain;
using CodeMuster.Infrastructure;
using CodeMuster.Mapping.CSharp;
using CodeMuster.Mapping.TypeScript;

namespace CodeMuster.Cli;

public static class Program
{
    public const string Usage = """
        usage: codemuster <verb> [options]
               codemuster --version

        verbs:
          init [--yes] [--no-gitignore]                     set this repo up: write .codemuster/config.json, gitignore the ledger
          doctor                                            check that git and the C# and TypeScript mappers work here;
                                                            prints the command that fixes each problem
          scan [--mode file]                                build or refresh the ledger: one flow per entry point,
                                                            or one unit per file with --mode file
          status                                            print coverage
          estimate [--path <folder>]                        approximate token cost of pending units
          next [--batch N] [--out <file>]                   print the next unit pack(s)
          done <unit> --fingerprint <fp> --findings <file>  record the model's response for a unit
          run --agent <name> [-j N] [--attempts N] [--force] [--kind <kind>] [--path <folder>] [--model <id>] [--effort <level>]
                                                            drive a headless agent over every pending unit, or only
                                                            one kind: file, slice, orphan, verify
                                                            agents: claude, codex, gemini, opencode, fake
                                                            model and effort go straight to the agent's own flags
          verify --agent <name> [-j N] [--attempts N] [--force] [--path <folder>] [--model <id>] [--effort <level>]
                                                            run --kind verify: try to refute recorded findings
          fix --agent <name> [--attempts N] [--path <folder>] [--model <id>] [--effort <level>]
                                                            fix the confirmed findings, one file at a time, and commit
                                                            each file it changes; needs a clean working tree
          report [--out <file>] [--include-refuted]         render findings and coverage as markdown;
                                                            refuted findings are left out unless asked for
          skill install --for <agent> [--global]            install the skill for claude, codex, gemini, or opencode

        every verb runs against the git repository containing the current directory.
        """;

    private static readonly string[] Verbs = ["init", "doctor", "scan", "status", "estimate", "next", "done", "run", "verify", "report", "skill", "fix"];

    private static readonly string[] KindNames = Enum.GetNames<UnitKind>().Select(name => name.ToLowerInvariant()).ToArray();

    public static async Task<int> Main(string[] args)
    {
        if (Console.IsOutputRedirected)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        }

        if (Console.IsErrorRedirected)
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
        }

        if (args is ["--version"])
        {
            Console.WriteLine(Version);
            return 0;
        }

        var command = CommandLine.Parse(args);
        if (command is null || !Verbs.Contains(command.Verb) || !HasRequiredArguments(command))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await RunAsync(command, cancellation.Token);
        }
        catch (NotInitializedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(Command command, CancellationToken cancellationToken)
    {
        var fileSystem = new PhysicalFileSystem();
        if (command.Verb == "skill")
        {
            var global = command.Flags.Contains("global");
            var path = await new SkillInstaller(fileSystem).InstallAsync(
                command.Options["for"],
                global,
                global ? "" : await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken),
                HomeDirectory(),
                EmbeddedSkill.Text,
                cancellationToken);
            Console.WriteLine($"installed skill to {path}");
            return 0;
        }

        if (command.Verb == "doctor")
        {
            return await DoctorAsync(fileSystem, cancellationToken);
        }

        var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        var tree = new GitSourceTree(repoRoot);
        if (command.Verb == "init")
        {
            return await InitAsync(command, repoRoot, fileSystem, tree, cancellationToken);
        }

        var config = await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken);
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), cancellationToken);
        var clock = new SystemClock();

        switch (command.Verb)
        {
            case "scan":
                return await ScanAsync(command, repoRoot, ledger, tree, clock, config, cancellationToken);
            case "status":
                Console.WriteLine((await new Status(ledger, config).RunAsync(cancellationToken)).Render());
                return 0;
            case "estimate":
                Console.WriteLine((await new Estimate(ledger, config, command.Options.GetValueOrDefault("path")).RunAsync(cancellationToken)).Render());
                return 0;
            case "next":
                return await NextAsync(command, ledger, tree, config, cancellationToken);
            case "done":
                var response = await File.ReadAllTextAsync(command.Options["findings"], cancellationToken);
                var done = await new Done(ledger, clock, config).RunAsync(command.Positionals[0], command.Options["fingerprint"], response, cancellationToken);
                Console.WriteLine(done.Message);
                return done.Outcome == DoneOutcome.Recorded ? 0 : 1;
            case "run":
            case "verify":
                return await RunAgentAsync(command, ledger, tree, clock, config, cancellationToken);
            case "fix":
                return await FixAsync(command, repoRoot, ledger, tree, clock, config, cancellationToken);
            default:
                var markdown = await new Report(ledger, config, command.Flags.Contains("include-refuted")).RunAsync(cancellationToken);
                if (command.Options.TryGetValue("out", out var reportPath))
                {
                    await File.WriteAllTextAsync(reportPath, markdown, cancellationToken);
                    Console.WriteLine($"wrote report to {reportPath}");
                }
                else
                {
                    Console.Write(markdown);
                }

                return 0;
        }
    }

    private static async Task<int> ScanAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, CancellationToken cancellationToken)
    {
        var mappers = command.Options.GetValueOrDefault("mode") == "file" ? [] : Mappers();
        var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), clock, config, mappers, repoRoot, new ProgressWriter(Console.Error)).RunAsync(cancellationToken);
        Console.WriteLine($"scanned {scan.FilesIncluded} files ({scan.FilesExcluded} excluded) at {scan.HeadCommit[..7]}: {scan.UnitsCreated} new, {scan.UnitsStale} stale, {scan.UnitsTotal} total units");
        if (scan.SliceMode is { } slices)
        {
            var resolution = slices.ResolutionRate is { } rate ? string.Create(CultureInfo.InvariantCulture, $", resolution {rate * 100:0.0}%") : "";
            Console.WriteLine($"{slices.Slices} slices, {slices.Orphans} orphans, {slices.Files} files{resolution}");
            foreach (var diagnostic in slices.Diagnostics)
            {
                Console.Error.WriteLine($"warning: {diagnostic}");
            }
        }

        return 0;
    }

    private static async Task<int> FixAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, CancellationToken cancellationToken)
    {
        var adapter = AgentAdapters.Create(
            command.Options["agent"],
            null,
            command.Options.GetValueOrDefault("model"),
            command.Options.GetValueOrDefault("effort"),
            write: true);
        var options = new FixOptions(
            int.Parse(command.Options.GetValueOrDefault("attempts", "3"), CultureInfo.InvariantCulture),
            command.Options.GetValueOrDefault("path"));
        Console.WriteLine($"fixing with {command.Options["agent"]}, one file at a time; each file it changes becomes a commit");
        var fix = new Fix(ledger, tree, clock, config, new GitWorkspace(repoRoot));
        var result = await fix.RunAsync(adapter, options, new ProgressWriter(Console.Out), cancellationToken);
        foreach (var unitId in result.GaveUp)
        {
            Console.Error.WriteLine($"gave up on {unitId} after {options.MaxAttempts} attempts");
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"fixed {result.Fixed} finding(s) across {result.Units} file(s), declined {result.Declined}, {result.GaveUp.Count} gave up"));
        return result.GaveUp.Count > 0 ? 1 : 0;
    }

    private static async Task<int> DoctorAsync(PhysicalFileSystem fileSystem, CancellationToken cancellationToken)
    {
        string repoRoot;
        try
        {
            repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine(DoctorReport.GitFailed(ex.Message).Render());
            return 1;
        }

        var config = File.Exists(ConfigLoader.PathFor(repoRoot)) ? await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken) : null;
        var report = await new Doctor(new GitSourceTree(repoRoot), Mappers(), new SystemClock(), repoRoot, config, new ProgressWriter(Console.Error)).RunAsync(cancellationToken);
        Console.WriteLine(report.Render());
        return report.Ready ? 0 : 1;
    }

    private static IReadOnlyList<ICodeMapper> Mappers() => [new RoslynMapper(), new TypeScriptMapper()];

    private static async Task<int> NextAsync(Command command, SqliteLedger ledger, GitSourceTree tree, Config config, CancellationToken cancellationToken)
    {
        var packs = await new Next(ledger, tree, config).RunAsync(int.Parse(command.Options.GetValueOrDefault("batch", "1")), cancellationToken);
        if (packs.Count == 0)
        {
            Console.Error.WriteLine("nothing pending; run status");
            return 0;
        }

        var text = string.Join('\n', packs.Select(pack => pack.Markdown));
        if (command.Options.TryGetValue("out", out var outPath))
        {
            await File.WriteAllTextAsync(outPath, text, cancellationToken);
            Console.WriteLine($"wrote {packs.Count} pack(s) to {outPath}");
        }
        else
        {
            Console.Write(text);
        }

        return 0;
    }

    private static async Task<int> RunAgentAsync(Command command, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, CancellationToken cancellationToken)
    {
        var template = Environment.GetEnvironmentVariable("CODEMUSTER_FAKE_RESPONSE");
        var adapter = AgentAdapters.Create(
            command.Options["agent"],
            template is null ? null : await File.ReadAllTextAsync(template, cancellationToken),
            command.Options.GetValueOrDefault("model"),
            command.Options.GetValueOrDefault("effort"));
        var kind = command.Verb == "verify" ? "verify" : command.Options.GetValueOrDefault("kind");
        var options = new RunOptions(
            int.Parse(command.Options.GetValueOrDefault("jobs", "1")),
            int.Parse(command.Options.GetValueOrDefault("attempts", "3")),
            command.Flags.Contains("force"),
            kind is null ? null : Enum.Parse<UnitKind>(kind, ignoreCase: true),
            command.Options.GetValueOrDefault("path"));
        Console.WriteLine($"running {command.Options["agent"]} on up to {options.Parallelism} unit(s) at a time; a line prints as each unit finishes");
        var result = await new Run(ledger, tree, clock, config, adapter, new RunProgressWriter(Console.Out), new ProgressWriter(Console.Out)).RunAsync(options, cancellationToken);
        foreach (var unitId in result.GaveUp)
        {
            Console.Error.WriteLine($"gave up on {unitId} after {options.MaxAttempts} attempts");
        }

        Console.WriteLine(result.Cancelled ? $"cancelled after {result.Completed} unit(s)" : $"completed {result.Completed} unit(s), {result.GaveUp.Count} gave up");
        return result.Cancelled || result.GaveUp.Count > 0 ? 1 : 0;
    }

    private static async Task<int> InitAsync(Command command, string repoRoot, PhysicalFileSystem fileSystem, GitSourceTree tree, CancellationToken cancellationToken)
    {
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        var result = await new Init(fileSystem, tree).RunAsync(repoRoot, _ => Task.FromResult(GitignorePrompt.Decide(command.Flags, interactive, () =>
        {
            Console.Write("add .codemuster/ledger.db to .gitignore? [Y/n] ");
            return Console.ReadLine();
        })), cancellationToken);
        Console.WriteLine(result.ConfigCreated ? "created .codemuster/config.json" : ".codemuster/config.json already present");
        Console.WriteLine(result.Gitignore switch
        {
            GitignoreOutcome.Appended => "added .codemuster/ledger.db to .gitignore",
            GitignoreOutcome.AlreadyCovered => ".gitignore already ignores .codemuster/ledger.db",
            _ => "left .gitignore alone; make sure .codemuster/ledger.db is ignored",
        });
        return 0;
    }

    private static bool HasRequiredArguments(Command command) => command.Verb switch
    {
        "init" => command.Positionals.Count == 0 && command.Options.Count == 0,
        "doctor" => command.Positionals.Count == 0 && command.Options.Count == 0 && command.Flags.Count == 0,
        "scan" => command.Flags.Count == 0 && command.Positionals.Count == 0 && command.Options.Keys.All(k => k == "mode") && command.Options.GetValueOrDefault("mode", "file") is "file" or "slice",
        "done" => command.Flags.Count == 0 && command.Positionals.Count == 1 && command.Options.ContainsKey("fingerprint") && command.Options.ContainsKey("findings"),
        "next" => command.Flags.Count == 0 && command.Positionals.Count == 0 && IsPositiveOrAbsent(command, "batch"),
        "run" => IsAgentRun(command, "kind") && (!command.Options.TryGetValue("kind", out var kind) || (KindNames.Contains(kind) && kind != "fix")),
        "verify" => IsAgentRun(command),
        "fix" => command.Positionals.Count == 0
            && command.Flags.Count == 0
            && command.Options.ContainsKey("agent")
            && command.Options.Keys.All(k => k is "agent" or "attempts" or "model" or "effort" or "path"),
        "estimate" => command.Flags.Count == 0 && command.Positionals.Count == 0 && command.Options.Keys.All(k => k == "path"),
        "report" => command.Positionals.Count == 0 && command.Options.Keys.All(k => k == "out") && command.Flags.All(f => f == "include-refuted"),
        "skill" => command.Positionals.SequenceEqual(["install"]) && SkillInstaller.Harnesses.Contains(command.Options.GetValueOrDefault("for", "")) && command.Flags.All(f => f == "global"),
        _ => command.Flags.Count == 0 && command.Positionals.Count == 0,
    };

    private static bool IsAgentRun(Command command, params string[] extraOptions) =>
        command.Positionals.Count == 0
        && command.Options.ContainsKey("agent")
        && command.Options.Keys.All(k => k is "agent" or "jobs" or "attempts" or "model" or "effort" or "path" || extraOptions.Contains(k))
        && command.Flags.All(f => f == "force")
        && IsPositiveOrAbsent(command, "jobs")
        && IsPositiveOrAbsent(command, "attempts");

    private static string Version =>
        (typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static bool IsPositiveOrAbsent(Command command, string option) =>
        !command.Options.TryGetValue(option, out var value) || (int.TryParse(value, out var n) && n > 0);
}
