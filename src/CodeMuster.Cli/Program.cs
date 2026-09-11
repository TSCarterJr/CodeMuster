using System.Text;
using CodeMuster.Application;
using CodeMuster.Infrastructure;

namespace CodeMuster.Cli;

public static class Program
{
    public const string Usage = """
        usage: codemuster <verb> [options]

        verbs:
          init [--yes] [--no-gitignore]                     set this repo up: write .codemuster/config.json, gitignore the ledger
          scan                                              build or refresh the ledger for this repo
          status                                            print coverage
          estimate                                          approximate token cost of pending units
          next [--batch N] [--out <file>]                   print the next unit pack(s)
          done <unit> --fingerprint <fp> --findings <file>  record the model's response for a unit
          run --agent <name> [-j N] [--attempts N] [--force]
                                                            drive a headless agent over every pending unit
                                                            agents: claude, codex, gemini, opencode, fake
          report [--out <file>]                             render findings and coverage as markdown
          skill install --for <agent> [--global]            install the skill for claude, codex, gemini, or opencode

        every verb runs against the git repository containing the current directory.
        """;

    private static readonly string[] Verbs = ["init", "scan", "status", "estimate", "next", "done", "run", "report", "skill"];

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
                var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), clock, config).RunAsync(cancellationToken);
                Console.WriteLine($"scanned {scan.FilesIncluded} files ({scan.FilesExcluded} excluded) at {scan.HeadCommit[..7]}: {scan.UnitsCreated} new, {scan.UnitsStale} stale, {scan.UnitsTotal} total units");
                return 0;
            case "status":
                Console.WriteLine((await new Status(ledger, config).RunAsync(cancellationToken)).Render());
                return 0;
            case "estimate":
                Console.WriteLine((await new Estimate(ledger, config).RunAsync(cancellationToken)).Render());
                return 0;
            case "next":
                return await NextAsync(command, ledger, tree, config, cancellationToken);
            case "done":
                var response = await File.ReadAllTextAsync(command.Options["findings"], cancellationToken);
                var done = await new Done(ledger, clock, config).RunAsync(command.Positionals[0], command.Options["fingerprint"], response, cancellationToken);
                Console.WriteLine(done.Message);
                return done.Outcome == DoneOutcome.Recorded ? 0 : 1;
            case "run":
                return await RunAgentAsync(command, ledger, tree, clock, config, cancellationToken);
            default:
                var markdown = await new Report(ledger, config).RunAsync(cancellationToken);
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
        var adapter = AgentAdapters.Create(command.Options["agent"], template is null ? null : await File.ReadAllTextAsync(template, cancellationToken));
        var options = new RunOptions(
            int.Parse(command.Options.GetValueOrDefault("jobs", "1")),
            int.Parse(command.Options.GetValueOrDefault("attempts", "3")),
            command.Flags.Contains("force"));
        var result = await new Run(ledger, tree, clock, config, adapter, new RunProgressWriter(Console.Out)).RunAsync(options, cancellationToken);
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
        "done" => command.Flags.Count == 0 && command.Positionals.Count == 1 && command.Options.ContainsKey("fingerprint") && command.Options.ContainsKey("findings"),
        "next" => command.Flags.Count == 0 && command.Positionals.Count == 0 && IsPositiveOrAbsent(command, "batch"),
        "run" => command.Positionals.Count == 0 && command.Options.ContainsKey("agent") && command.Options.Keys.All(k => k is "agent" or "jobs" or "attempts") && command.Flags.All(f => f == "force") && IsPositiveOrAbsent(command, "jobs") && IsPositiveOrAbsent(command, "attempts"),
        "skill" => command.Positionals.SequenceEqual(["install"]) && SkillInstaller.Harnesses.Contains(command.Options.GetValueOrDefault("for", "")) && command.Flags.All(f => f == "global"),
        _ => command.Flags.Count == 0 && command.Positionals.Count == 0,
    };

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static bool IsPositiveOrAbsent(Command command, string option) =>
        !command.Options.TryGetValue(option, out var value) || (int.TryParse(value, out var n) && n > 0);
}
