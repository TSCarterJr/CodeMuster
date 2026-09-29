using System.Globalization;
using System.Reflection;
using System.Text;
using CodeMuster.Application;
using CodeMuster.Domain;
using CodeMuster.Infrastructure;
using CodeMuster.Infrastructure.Audits;
using CodeMuster.Mapping.CSharp;
using CodeMuster.Mapping.TypeScript;

namespace CodeMuster.Cli;

public static class Program
{
    public const string Usage = HelpText.Overview;

    public static async Task<int> Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            // Programs started by bare name (Roslyn's build host starts dotnet.exe; npm's cmd-shims start node) are otherwise looked for
            // in the current directory, usually the repository being audited. Children inherit this, so it covers the shims' cmd.exe too.
            Environment.SetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath", "1");
        }

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

        if (args is ["--help"] or ["-h"] or ["help"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var helpCommand = args is ["help", var requested] ? requested
            : args.Length > 1 && args.Skip(1).Any(arg => arg is "--help" or "-h") ? args[0] : null;
        if (helpCommand is not null && HelpText.For(helpCommand.ToLowerInvariant()) is { } help)
        {
            Console.WriteLine(help);
            return 0;
        }

        if (args.Length == 0)
        {
            // At a terminal a bare codemuster opens auto (D85); a script gets help and never starts spending.
            if (!AtTerminal(args))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            args = ["auto"];
        }

        if (args[0].Equals("hook", StringComparison.OrdinalIgnoreCase))
        {
            // A usage error would exit 2, which fails the agent's tool call, so arguments a hand-written hook adds are only named.
            if (args.Length > 1) Console.Error.WriteLine($"warning: codemuster hook takes no arguments; ignoring {string.Join(' ', args.Skip(1))}");
            return await HookAsync(new PhysicalFileSystem(), CancellationToken.None);
        }

        Command command;
        var interactive = AtTerminal(args);
        try
        {
            command = CommandLine.Parse(args, interactive);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(ex.Usage);
            return 2;
        }

        // The fake agent commits placeholder edits in fix mode, so only the test suite and the package smoke may use it.
        if (command.Options.TryGetValue("agent", out var agent) && agent == "fake" && Environment.GetEnvironmentVariable("CODEMUSTER_TEST_AGENT") != "1")
        {
            Console.Error.WriteLine($"error: unknown agent 'fake'; choose one of {string.Join(", ", AgentAdapters.Names)}");
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
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
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
            return await DoctorAsync(command, fileSystem, cancellationToken);
        }

        if (command.Verb == "mcp")
        {
            return await McpAsync(command.Flags.Contains("refresh"), fileSystem, cancellationToken);
        }

        if (command.Verb == "auto")
        {
            return await AutoAsync(command, fileSystem, cancellationToken);
        }

        var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        var tree = new GitSourceTree(repoRoot);
        if (command.Verb is "run" or "verify" or "fix" && !command.Options.ContainsKey("agent"))
        {
            command = AskForAgent(command, repoRoot);
        }

        if (command.Verb == "init")
        {
            return await InitAsync(command, repoRoot, fileSystem, tree, cancellationToken);
        }

        using var coordinator = command.Verb is "scan" or "run" or "verify" or "fix" or "next" or "done" or "validate" or "intelligent-config"
            ? await CoordinatorLock.AcquireAsync(repoRoot, cancellationToken) : null;
        var config = await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken);
        if (command.Verb == "intelligent-config")
            return await IntelligentConfigAsync(command, repoRoot, fileSystem, tree, cancellationToken);
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), cancellationToken);
        var clock = new SystemClock();
        using var events = command.Verb is "scan" or "run" or "verify" or "fix" ? EngineEvents(clock) : null;
        var control = command.Verb is "run" or "verify" or "fix" ? EngineControl() : null;
        var changes = ChangeTracker(repoRoot, config);
        if (command.Verb is "status" or "report" or "fix" or "verify" or "run" or "next")
        {
            await WarnAboutChangesAsync(changes, cancellationToken);
        }

        switch (command.Verb)
        {
            case "scan":
                var snapshot = await changes.SnapshotAsync(cancellationToken);
                var exit = await ScanAsync(command, repoRoot, ledger, tree, clock, config, events, cancellationToken);
                if (exit == 0) await changes.AcknowledgeAsync(snapshot, cancellationToken);
                return exit;
            case "status":
                Console.WriteLine((await new Status(ledger, config).RunAsync(cancellationToken)).Render());
                return 0;
            case "estimate":
                Console.WriteLine((await new Estimate(ledger, config, command.Options.GetValueOrDefault("path")).RunAsync(cancellationToken)).Render());
                return 0;
            case "next":
                return await NextAsync(command, ledger, tree, config, cancellationToken);
            case "done":
                var response = await ReadOptionFileAsync("findings", command.Options["findings"], cancellationToken);
                var done = await new Done(ledger, clock, config, fileSystem: fileSystem, repoRoot: repoRoot, tree: tree, hasher: new GitBlobHasher(repoRoot)).RunAsync(command.Positionals[0], command.Options["fingerprint"], response, cancellationToken);
                if (done.Outcome == DoneOutcome.Recorded)
                {
                    Console.WriteLine(done.Message);
                    return 0;
                }

                // The ledger keeps the parser's full text; the console gets where the file stops being JSON, counted from 1.
                Console.Error.WriteLine("error: " + (done.Outcome == DoneOutcome.InvalidResponse && JsonPosition(response) is { } position
                    ? $"--findings {command.Options["findings"]} is not valid JSON ({position}); correct it and run the same done command"
                    : done.Message));
                return 1;
            case "run":
            case "verify":
                return await RunAgentAsync(command, repoRoot, ledger, tree, clock, config, events, control, cancellationToken);
            case "validate":
                ITestRunner? runner = config.TestCommand.Count == 0 ? null : new CommandTestRunner(repoRoot, config.TestCommand);
                var validation = await new Validate(runner).RunAsync(cancellationToken);
                Console.WriteLine(validation.Output + validation.Error);
                Console.WriteLine(validation.Passed ? "validation passed" : "validation failed");
                return validation.Passed ? 0 : 1;
            case "fix":
                return await FixAsync(command, repoRoot, ledger, tree, clock, config, events, control, cancellationToken);
            case "map":
                return await MapAsync(command, ledger, tree, repoRoot, cancellationToken);
            case "impact":
                var impact = await new ImpactQuery(ledger, tree).RunAsync(command.Options.GetValueOrDefault("since"),
                    command.Options.GetValueOrDefault("format") == "json" ? MapFormat.Json : MapFormat.Text, cancellationToken);
                if (impact.ExitCode != 0)
                {
                    Console.Error.WriteLine("error: " + impact.Error);
                    return impact.ExitCode;
                }

                Console.Write(impact.Output);
                return 0;
            default:
                var markdown = await new Report(ledger, config, command.Flags.Contains("include-refuted")).RunAsync(cancellationToken);
                if (command.Options.TryGetValue("out", out var reportPath))
                {
                    await WriteOptionFileAsync("out", reportPath, markdown, cancellationToken);
                    Console.WriteLine($"wrote report to {reportPath}");
                }
                else
                {
                    Console.Write(markdown);
                }

                return 0;
        }
    }

    // The hidden engine stream (D65): written only when CODEMUSTER_ENGINE_EVENTS names a file, and never mentioned in help.
    private static EngineEventFile? EngineEvents(IClock clock) =>
        Environment.GetEnvironmentVariable("CODEMUSTER_ENGINE_EVENTS") is { Length: > 0 } path ? new EngineEventFile(path, clock) : null;

    // The hidden control channel (D65): commands appended to the file named by CODEMUSTER_ENGINE_CONTROL.
    private static EngineControlFile? EngineControl() =>
        Environment.GetEnvironmentVariable("CODEMUSTER_ENGINE_CONTROL") is { Length: > 0 } path ? new EngineControlFile(path) : null;

    // Test-only: holds every fake agent call open, so end-to-end tests can send engine commands while units run.
    private static TimeSpan FakeDelay() =>
        int.TryParse(Environment.GetEnvironmentVariable("CODEMUSTER_FAKE_DELAY_MS"), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;

    private static async Task<int> ScanAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, IEngineEvents? events, CancellationToken cancellationToken)
    {
        var mappers = command.Options.GetValueOrDefault("mode") == "file" ? [] : Mappers();
        var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), clock, config, mappers, repoRoot, new ProgressWriter(Console.Error), new DependencyAuditor(), events, command.Flags.Contains("remap"))
            .RunAsync(cancellationToken);
        Console.WriteLine($"scanned {scan.FilesIncluded} files ({scan.FilesExcluded} excluded) at {scan.HeadCommit[..7]}: {scan.UnitsCreated} new, {scan.UnitsStale} stale, {scan.UnitsTotal} total units");
        if (scan.Vulnerabilities is { } audit)
        {
            if (audit.Packages > 0)
            {
                var bySeverity = audit.BySeverity
                    .OrderBy(entry => entry.Key)
                    .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Value} {entry.Key.ToString().ToLowerInvariant()}"));
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{audit.Packages} vulnerable package(s) across {audit.Manifests} manifest(s): {string.Join(", ", bySeverity)}"));
            }

            foreach (var diagnostic in audit.Diagnostics)
            {
                Console.Error.WriteLine($"warning: {diagnostic}");
            }
        }

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

    private static async Task<int> FixAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, IEngineEvents? events, IEngineControl? control, CancellationToken cancellationToken)
    {
        if (config.TestCommand.Count > 0)
        {
            // Checked before the preview and even with --allow-failing-tests: every repair would fail to validate, after a paid agent call.
            try
            {
                ExecutableResolver.Resolve(config.TestCommand[0]);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"test_command cannot start, so no agent was called: {ex.Message} Fix test_command in .codemuster/config.json; codemuster validate runs it.", ex);
            }
        }

        var identity = await ResolveAgentAsync(command, repoRoot, cancellationToken);
        var adapter = AgentAdapters.Create(identity.Agent, null, identity.Model, identity.Effort, write: true, workingDirectory: repoRoot);
        var workspace = new GitWorkspace(repoRoot);
        var stash = command.Flags.Contains("stash");
        if (!await workspace.IsCleanAsync(cancellationToken))
        {
            var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
            stash = StashPrompt.Decide(command.Flags, interactive, () =>
            {
                Console.Write("tracked files have local changes. Stash them, fix findings, then restore them? Untracked files stay in place. [y/N] ");
                return Console.ReadLine();
            });
            if (!stash && interactive)
            {
                Console.WriteLine("left your changes alone; no fixes started");
                return 1;
            }
        }

        var options = new FixOptions(
            int.Parse(command.Options.GetValueOrDefault("attempts", "3"), CultureInfo.InvariantCulture),
            command.Options.GetValueOrDefault("path"),
            stash,
            int.Parse(command.Options.GetValueOrDefault("jobs", "1"), CultureInfo.InvariantCulture),
            command.Flags.Contains("retry-declined"))
        {
            RelatedFiles = command.Options.TryGetValue("include-related", out var related) ? related.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(RepoPath.Normalize).ToArray() : [],
            AllowFailingTests = command.Flags.Contains("allow-failing-tests"),
            IncludeSimplification = command.Options.GetValueOrDefault("include") == Config.SimplificationCategory,
        };
        if (options.RelatedFiles.Count > 0)
        {
            var tracked = (await tree.ListFilesAsync(cancellationToken)).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            if (options.Parallelism != 1 || options.Path is null || !tracked.Contains(options.Path) || options.RelatedFiles.Any(p => !tracked.Contains(p)))
                throw new ArgumentException("--include-related requires -j 1, an exact tracked --path, and exact existing tracked related files");
        }
        await PreviewAgentAsync(adapter.Identity, options.Parallelism, clock, cancellationToken, !command.Flags.Contains("yes"));
        var concurrency = options.Parallelism == 1 ? "one file at a time" : string.Create(CultureInfo.InvariantCulture, $"up to {options.Parallelism} files at a time");
        Console.WriteLine($"fixing with {command.Options["agent"]}, {concurrency}; each file it changes becomes a commit");
        ITestRunner? tests = config.TestCommand.Count > 0 ? new CommandTestRunner(repoRoot, config.TestCommand) : null;
        if (tests is null)
        {
            Console.Error.WriteLine("warning: no test_command in .codemuster/config.json, so nothing checks that a fix still builds");
        }

        var progress = new ProgressWriter(Console.Out, ConsoleStyle());
        // An engine command may change the model or effort for files not yet started (D65); for codex the other setting keeps its resolved value (D55).
        using var fileFixer = new GitFileFixer(repoRoot, (directory, requested) => AgentAdapters.Create(
            identity.Agent, null, (requested ?? identity).Model, (requested ?? identity).Effort, write: true, workingDirectory: directory, fakeDelay: FakeDelay()), progress, options.RelatedFiles);
        var fix = new Fix(ledger, tree, clock, config, workspace, tests, fileFixer, new GitBlobHasher(repoRoot), events, control);
        var result = await fix.RunAsync(adapter, options, progress, cancellationToken);
        foreach (var unitId in result.GaveUp)
        {
            Console.Error.WriteLine($"gave up on {unitId}");
        }

        var skipped = result.Skipped.Count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $", {result.Skipped.Count} skipped");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"fixed {result.Fixed} finding(s) across {result.Units} file(s), declined {result.Declined}{skipped}, {result.GaveUp.Count} gave up"));
        return result.GaveUp.Count > 0 ? 1 : 0;
    }

    private static async Task<int> DoctorAsync(Command command, PhysicalFileSystem fileSystem, CancellationToken cancellationToken)
    {
        var fix = command.Flags.Contains("fix");
        var commands = new ProcessCommandRunner();
        var (root, report) = await DiagnoseAsync(fileSystem, commands, cancellationToken);
        Console.WriteLine(report.Render());
        if (!fix || report.Fixes.Count == 0)
        {
            return report.Ready ? 0 : 1;
        }

        var mode = command.Flags.Contains("yes") ? DoctorFixMode.All
            : !Console.IsInputRedirected && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ? DoctorFixMode.Ask
            : DoctorFixMode.PrintOnly;
        var output = new LineWriter(Console.Out);
        var started = await new DoctorFixer(commands, root, output).ApplyAsync(report.Fixes, mode, candidate =>
        {
            Console.Write($"run {candidate.Render()}? [y/N] ");
            return Console.ReadLine()?.Trim() is { } answer && (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }, cancellationToken);
        if (started == 0)
        {
            return report.Ready ? 0 : 1;
        }

        Console.WriteLine("checking again:");
        (_, report) = await DiagnoseAsync(fileSystem, commands, cancellationToken);
        Console.WriteLine(report.Render());
        return report.Ready ? 0 : 1;
    }

    // A folder outside any repository is its own root, so doctor --fix can make it one there.
    private static async Task<(string Root, DoctorReport Report)> DiagnoseAsync(PhysicalFileSystem fileSystem, ProcessCommandRunner commands, CancellationToken cancellationToken)
    {
        string repoRoot;
        try
        {
            repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        }
        catch (NotARepositoryException ex)
        {
            return (Directory.GetCurrentDirectory(), DoctorReport.NotARepository(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Directory.GetCurrentDirectory(), DoctorReport.GitFailed(ex.Message));
        }

        var config = File.Exists(ConfigLoader.PathFor(repoRoot)) ? await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken) : null;
        var report = await new Doctor(new GitSourceTree(repoRoot), Mappers(), new SystemClock(), repoRoot, config, new ProgressWriter(Console.Error), commands).RunAsync(cancellationToken);
        return (repoRoot, report);
    }

    private sealed class LineWriter(TextWriter writer) : IProgress<string>
    {
        public void Report(string value) => writer.WriteLine(value);
    }

    private static async Task<int> HookAsync(PhysicalFileSystem fileSystem, CancellationToken cancellationToken)
    {
        try
        {
            var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
            if (fileSystem.FileExists(ConfigLoader.PathFor(repoRoot)))
            {
                await new GitChangeTracker(repoRoot).NotifyAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A nonzero exit would fail the agent's tool call, which costs more than one missed notification.
            Console.Error.WriteLine($"warning: codemuster hook could not record the change: {ex.Message}");
        }

        Console.WriteLine("{}");
        return 0;
    }

    private static GitChangeTracker ChangeTracker(string repoRoot, Config config) =>
        new(repoRoot, config.AffectsScan);

    private static async Task WarnAboutChangesAsync(GitChangeTracker changes, CancellationToken cancellationToken)
    {
        try
        {
            if (await changes.ChangesAsync(cancellationToken) is { Detected: true } changed)
            {
                Console.Error.WriteLine(ChangeWarning(changed.Paths));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The warning is advisory: a file git cannot read, or a clean filter that fails, must not stop the command it precedes.
            Console.Error.WriteLine($"warning: could not check for changes since the last scan: {ex.Message}");
        }
    }

    private static string ChangeWarning(IReadOnlyList<string> paths)
    {
        const string Advice = "run codemuster scan to refresh coverage";
        return paths.Count switch
        {
            0 => $"files changed since the last scan; {Advice}",
            <= 3 => $"{string.Join(", ", paths)} changed since the last scan; {Advice}",
            _ => string.Create(CultureInfo.InvariantCulture, $"{paths.Count} files changed since the last scan, including {string.Join(", ", paths.Take(3))}; {Advice}"),
        };
    }

    private static IReadOnlyList<ICodeMapper> Mappers() => [new RoslynMapper(), new TypeScriptMapper(() => ExecutableResolver.Resolve("node"), ChildProcesses.Start)];

    private static async Task<int> NextAsync(Command command, SqliteLedger ledger, GitSourceTree tree, Config config, CancellationToken cancellationToken)
    {
        var requestedKind = command.Options.GetValueOrDefault("kind");
        var packs = await new Next(ledger, tree, config, kind: requestedKind is null ? null : Enum.Parse<UnitKind>(requestedKind, true), path: command.Options.GetValueOrDefault("path"), notes: new ProgressWriter(Console.Error)).RunAsync(int.Parse(command.Options.GetValueOrDefault("batch", "1")), cancellationToken);
        if (packs.Count == 0)
        {
            Console.Error.WriteLine("nothing pending; run status");
            return 0;
        }

        var text = string.Join('\n', packs.Select(pack => pack.Markdown));
        if (command.Options.TryGetValue("out", out var outPath))
        {
            await WriteOptionFileAsync("out", outPath, text, cancellationToken);
            Console.WriteLine($"wrote {packs.Count} pack(s) to {outPath}");
        }
        else
        {
            Console.Write(text);
        }

        return 0;
    }

    private static async Task<int> MapAsync(Command command, SqliteLedger ledger, GitSourceTree tree, string repoRoot, CancellationToken cancellationToken)
    {
        var outPath = command.Options.GetValueOrDefault("out");
        var references = command.Positionals.ElementAtOrDefault(0) == "references";
        var page = !references && outPath is not null && outPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
        var format = command.Options.GetValueOrDefault("format") switch
        {
            "mermaid" => MapFormat.Mermaid,
            "json" => MapFormat.Json,
            _ => MapFormat.Text,
        };
        int? depth = command.Options.TryGetValue("depth", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : null;
        var request = new MapRequest(command.Positionals.ElementAtOrDefault(0), command.Positionals.ElementAtOrDefault(1), depth, format, page);
        var result = references
            ? await new McpTools(ledger, tree, new GitBlobHasher(repoRoot)).ReferencesAsync(command.Positionals[1], command.Options.GetValueOrDefault("kind"), format, cancellationToken)
            : await new CodeMapQuery(ledger).RunAsync(request, cancellationToken);
        if (result.ExitCode != 0)
        {
            Console.Error.WriteLine("error: " + result.Error);
            return result.ExitCode;
        }

        var output = page ? MapPage.Render(result.Output) : result.Output;
        if (outPath is null)
        {
            Console.Write(output);
            return 0;
        }

        await WriteOptionFileAsync("out", outPath, output, cancellationToken);
        Console.WriteLine($"wrote map to {outPath}");
        return 0;
    }

    private static async Task<int> McpAsync(bool refresh, PhysicalFileSystem fileSystem, CancellationToken cancellationToken)
    {
        // Only protocol messages may reach standard output, so anything else written through Console goes to standard error.
        var protocol = Console.Out;
        Console.SetOut(Console.Error);
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        string? repoRoot = null;
        var problem = "";
        try
        {
            repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The agent starts the server wherever it runs, so outside a repository the tools say why instead of the server failing to start.
            problem = "codemuster mcp needs a Git repository: " + ex.Message;
        }

        SqliteLedger? ledger = null;
        McpTools? tools = null;
        try
        {
            await new McpServer(input, protocol, Console.Error, Version, CallAsync).RunAsync(cancellationToken);
        }
        finally
        {
            ledger?.Dispose();
        }

        return 0;

        async Task<McpToolResult> CallAsync(string name, System.Text.Json.Nodes.JsonObject? arguments, CancellationToken token)
        {
            if (repoRoot is null)
            {
                return McpToolResult.Failure(problem);
            }

            if (tools is null)
            {
                // Opened on the first call after init and scan, so a server started before them still works without creating a ledger itself.
                var path = Path.Combine(repoRoot, ".codemuster", "ledger.db");
                if (!File.Exists(ConfigLoader.PathFor(repoRoot))) return McpToolResult.Failure("CodeMuster is not set up in this repository; run codemuster init, then codemuster scan");
                if (!File.Exists(path)) return McpToolResult.Failure("no code map yet; run codemuster scan, then call this tool again");
                ledger = await SqliteLedger.OpenAsync(path, token);
                var tree = new GitSourceTree(repoRoot);
                var root = repoRoot;
                var opened = ledger;
                tools = new McpTools(ledger, tree, new GitBlobHasher(repoRoot), refresh ? ct => RefreshAsync(root, fileSystem, opened, tree, ct) : null);
            }

            return await tools.CallAsync(name, arguments, token);
        }
    }

    // mcp --refresh: scan before answering when the map is stale, unless another command holds the coordinator lock.
    private static async Task<string> RefreshAsync(string repoRoot, PhysicalFileSystem fileSystem, SqliteLedger ledger, GitSourceTree tree, CancellationToken cancellationToken)
    {
        IDisposable coordinator;
        try
        {
            coordinator = await CoordinatorLock.AcquireAsync(repoRoot, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return "refresh skipped: another CodeMuster command is using this repository, so these results come from the stored map";
        }

        using (coordinator)
        {
            try
            {
                var config = await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken);
                var changes = ChangeTracker(repoRoot, config);
                var snapshot = await changes.SnapshotAsync(cancellationToken);
                var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), new SystemClock(), config, Mappers(), repoRoot, new ProgressWriter(Console.Error), new DependencyAuditor())
                    .RunAsync(cancellationToken);
                await changes.AcknowledgeAsync(snapshot, cancellationToken);
                return $"refreshed: scanned {scan.FilesIncluded} files at {scan.HeadCommit[..7]} before answering";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"refresh failed, so these results come from the stored map: {ex.Message}";
            }
        }
    }

    private static async Task<int> RunAgentAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, IEngineEvents? events, IEngineControl? control, CancellationToken cancellationToken)
    {
        var template = Environment.GetEnvironmentVariable("CODEMUSTER_FAKE_RESPONSE");
        var templateJson = template is null ? null : await File.ReadAllTextAsync(template, cancellationToken);
        var identity = await ResolveAgentAsync(command, repoRoot, cancellationToken);
        // An engine command may change the model or effort for units not yet started (D65); for codex the other setting keeps its resolved value (D55).
        IAgentAdapter Agent(AgentIdentity requested) =>
            AgentAdapters.Create(requested.Agent, templateJson, requested.Model, requested.Effort, workingDirectory: repoRoot, fakeDelay: FakeDelay());
        var adapter = Agent(identity);
        var kind = command.Verb == "verify" ? "verify" : command.Options.GetValueOrDefault("kind");
        var options = new RunOptions(
            int.Parse(command.Options.GetValueOrDefault("jobs", "1")),
            int.Parse(command.Options.GetValueOrDefault("attempts", "3")),
            command.Flags.Contains("force"),
            kind is null ? null : Enum.Parse<UnitKind>(kind, ignoreCase: true),
            command.Options.GetValueOrDefault("path"))
        {
            SkipVerify = command.Flags.Contains("no-verify"),
        };
        await PreviewAgentAsync(adapter.Identity, options.Parallelism, clock, cancellationToken, !command.Flags.Contains("yes"));
        if (kind == "verify") await new RefreshVerification(ledger, tree, config, new GitBlobHasher(repoRoot)).RunAsync(cancellationToken);
        Console.WriteLine($"running {command.Options["agent"]} on up to {options.Parallelism} unit(s) at a time; a line prints as each unit finishes");
        var result = await new Run(ledger, tree, clock, config, adapter, new RunProgressWriter(Console.Out, ConsoleStyle()), new ProgressWriter(Console.Out, ConsoleStyle()), events, control, Agent).RunAsync(options, cancellationToken);
        foreach (var unitId in result.GaveUp)
        {
            Console.Error.WriteLine($"gave up on {unitId} after {options.MaxAttempts} attempts");
        }

        var skipped = result.Skipped.Count == 0 ? "" : $", {result.Skipped.Count} skipped";
        Console.WriteLine((result.Cancelled ? $"cancelled after {result.Completed} unit(s)" : $"completed {result.Completed} unit(s), {result.GaveUp.Count} gave up") + skipped);
        return result.Cancelled || result.GaveUp.Count > 0 ? 1 : 0;
    }

    private static Task<AgentIdentity> ResolveAgentAsync(Command command, string repoRoot, CancellationToken cancellationToken) =>
        CodexSettingsResolver.ResolveAsync(new AgentIdentity(command.Options.GetValueOrDefault("agent", "codex"),
            command.Options.GetValueOrDefault("model"), command.Options.GetValueOrDefault("effort")), repoRoot, cancellationToken);

    // With --yes the preview prints without its countdown: the person already said go.
    private static Task PreviewAgentAsync(AgentIdentity identity, int parallelism, IClock clock, CancellationToken cancellationToken, bool countdown = true)
    {
        var style = ConsoleStyle();
        var interactive = countdown && style.Enabled && !Console.IsInputRedirected;
        return new AgentStartPreview(Console.Error, clock,
            () => Console.KeyAvailable ? Console.ReadKey(intercept: true).Key : null,
            Task.Delay, style).RunAsync(identity, parallelism, interactive, cancellationToken);
    }

    private static TerminalStyle ConsoleStyle()
    {
        var redirected = Console.IsOutputRedirected || Console.IsErrorRedirected;
        var width = 80;
        if (!redirected)
        {
            try { width = Console.WindowWidth; }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }
        }
        return TerminalStyle.Detect(redirected, !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")),
            Environment.GetEnvironmentVariable("TERM"), Environment.GetEnvironmentVariable("NO_COLOR"), width > 0 ? width : 80);
    }

    private static async Task<int> IntelligentConfigAsync(Command command, string repoRoot, IFileSystem fileSystem, ISourceTree tree, CancellationToken cancellationToken)
    {
        var identity = await ResolveAgentAsync(command, repoRoot, cancellationToken);
        var (name, model, effort) = identity;
        var template = Environment.GetEnvironmentVariable("CODEMUSTER_FAKE_RESPONSE");
        IAgentAdapter adapter = name == "fake"
            ? new FakeAgentAdapter(template is null ? "{\"changes\":{},\"reasons\":{}}" : await fileSystem.ReadAllTextAsync(template, cancellationToken), model, effort, rawResponse: true)
            : AgentAdapters.Create(name, null, model, effort, workingDirectory: repoRoot);
        var clock = new SystemClock();
        await PreviewAgentAsync(adapter.Identity, 1, clock, cancellationToken);
        Console.WriteLine("inspecting repository structure and configuration with the selected agent...");
        var result = await new AgentActivity(Console.Error, clock, ConsoleStyle(), Task.Delay).RunAsync(
            $"Inspecting repository with {name.ToUpperInvariant()}",
            token => new IntelligentConfig(tree, fileSystem, adapter, clock).RunAsync(repoRoot, token), cancellationToken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"inspected {result.TrackedFiles} tracked file paths with bounded manifest/source samples"));
        foreach (var change in result.Changes) Console.WriteLine($"- {change}");
        Console.WriteLine(result.SpendLine());
        if (result.Changed)
        {
            Console.WriteLine($"updated .codemuster/config.json; backup: {result.BackupPath}");
            Console.WriteLine("run codemuster scan to apply the new audit scope; configured test commands have not been executed");
        }
        else Console.WriteLine("no configuration changes needed");
        return 0;
    }

    private static async Task<int> InitAsync(Command command, string repoRoot, PhysicalFileSystem fileSystem, GitSourceTree tree, CancellationToken cancellationToken)
    {
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        var selection = command.Options.GetValueOrDefault("for");
        if (selection is null && !command.Flags.Contains("no-skills") && interactive && !command.Flags.Contains("yes"))
        {
            Console.Write("install skills, hooks and the MCP server for which agents? [claude,codex,gemini / none; default all] ");
            selection = Console.ReadLine();
            selection = string.IsNullOrWhiteSpace(selection) ? "all" : selection.Trim();
        }
        selection ??= command.Flags.Contains("yes") ? "all" : "none";
        var agents = command.Flags.Contains("no-skills") || selection == "none" ? Array.Empty<string>() : AgentSetup.Select(selection);
        var setup = await new AgentSetup(fileSystem).InstallAsync(repoRoot, agents, !command.Flags.Contains("no-hooks"), EmbeddedSkill.Text, cancellationToken);
        foreach (var agentSetup in setup) Console.WriteLine(SetupLine(repoRoot, agentSetup));
        var servers = command.Flags.Contains("no-mcp") ? [] : await new AgentSetup(fileSystem).InstallMcpAsync(repoRoot, agents, cancellationToken);
        foreach (var server in servers) Console.WriteLine(McpLine(repoRoot, server));
        if (agents.Count == 0) Console.WriteLine("no agent skills selected; use init --for claude,codex,gemini to install them");
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

    private static string? JsonPosition(string text)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            return string.Create(CultureInfo.InvariantCulture, $"line {ex.LineNumber + 1}, column {ex.BytePositionInLine + 1}");
        }
    }

    private static async Task<string> ReadOptionFileAsync(string option, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException(FileProblem(option, path, ex), ex);
        }
    }

    private static async Task WriteOptionFileAsync(string option, string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(path, text, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException(FileProblem(option, path, ex), ex);
        }
    }

    // Names the option and the path as typed instead of .NET's text with the absolute path; the wording differs between Windows and Unix.
    private static string FileProblem(string option, string path, Exception ex) =>
        Directory.Exists(path) ? $"--{option} {path} is a folder; name a file"
        : ex is FileNotFoundException ? $"--{option} {path}: no such file"
        : ex is DirectoryNotFoundException ? $"--{option} {path}: folder {Path.GetDirectoryName(path)} does not exist"
        : ex is UnauthorizedAccessException ? $"--{option} {path}: permission denied"
        : $"--{option} {path}: {ex.Message}";

    private static string SetupLine(string repoRoot, AgentSetupResult result)
    {
        if (result.Hook == HookChange.Current && !result.SkillWritten)
        {
            return $"{result.Agent} skill and change hook already current";
        }

        var skill = result.SkillWritten ? $"installed {result.Agent} skill" : $"{result.Agent} skill already current";
        var settings = result.SettingsPath is null ? "" : RepoPath.Normalize(Path.GetRelativePath(repoRoot, result.SettingsPath));
        var rewrote = result.RewroteSettings ? ", which rewrites that file without its comments" : "";
        return skill + result.Hook switch
        {
            HookChange.Added => $"; added its change hook to {settings}{rewrote} (reload the agent and approve hooks if prompted)",
            HookChange.Repaired => $"; repaired its change hook's matcher and timeout in {settings}{rewrote} (reload the agent and approve hooks if prompted)",
            HookChange.Current => "; change hook already current",
            _ => "",
        };
    }

    private static string McpLine(string repoRoot, McpSetupResult result)
    {
        var path = RepoPath.Normalize(Path.GetRelativePath(repoRoot, result.Path));
        return result.Added
            ? $"added the codemuster MCP server to {path} for {result.Agent}{(result.RewroteSettings ? ", which rewrites that file without its comments" : "")} (approve it when the agent asks)"
            : $"codemuster MCP server already in {path} for {result.Agent}";
    }

    private static string Version =>
        (typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    // The audit flow in one command (D85): choose the steps and the agent once, then run each step as its own command, stopping at the first that fails.
    private static async Task<int> AutoAsync(Command command, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) && !command.Flags.Contains("yes");
        var store = new ChoiceStore(StateDirectory());
        var remembered = store.Load(repoRoot);
        var named = command.Options.ContainsKey("steps") || command.Options.ContainsKey("skip");
        if (!interactive && !named && !command.Flags.Contains("yes")) return AutoMistake("no terminal to ask which steps to run; pass --steps, --skip or --yes");
        if (command.Options.TryGetValue("max-cost", out var limitText) && (!decimal.TryParse(limitText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0))
            return AutoMistake($"--max-cost must be a positive number of dollars (got \"{limitText}\")");

        var configured = File.Exists(Path.Combine(repoRoot, ".codemuster", "config.json"));
        if (!configured && interactive)
        {
            Console.Write("This repository has no CodeMuster setup yet. Run codemuster init now? [Y/n] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() is "n" or "no") return 1;
            var init = await RunAsync(CommandLine.Parse(["init"]), cancellationToken);
            if (init != 0) return init;
        }

        IReadOnlyList<AutoStep> steps;
        try
        {
            steps = named || !interactive
                ? AutoSteps.Select(command.Options.GetValueOrDefault("steps"), command.Options.GetValueOrDefault("skip"))
                : new StepMenu(Console.In, Console.Out).Choose(remembered?.Steps is { } names ? AutoSteps.Select(string.Join(',', names), null) : AutoSteps.Defaults);
        }
        catch (ArgumentException ex)
        {
            return AutoMistake(ex.Message);
        }

        if (steps.Count == 0) return AutoMistake("no steps are left to run");
        if (AutoSteps.FixWarning([.. steps], await ConfirmedFindingsAsync(repoRoot, cancellationToken)) is { } warning) Console.Error.WriteLine("warning: " + warning);

        var agentOptions = command.Options.Where(o => o.Key is "agent" or "jobs" or "model" or "effort").ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);
        if (AutoSteps.NeedsAgent(steps) && !agentOptions.ContainsKey("agent"))
        {
            if (!interactive) return AutoMistake($"--agent is required for run, verify and fix ({string.Join(", ", AgentAdapters.Names)})");
            var answers = new AgentQuestions(Console.In, Console.Out).Ask(AgentAdapters.Installed(), remembered, agentOptions);
            agentOptions["agent"] = answers.Agent;
            agentOptions["jobs"] = answers.Jobs.ToString(CultureInfo.InvariantCulture);
            if (answers.Model is { } model) agentOptions["model"] = model;
            if (answers.Effort is { } effort) agentOptions["effort"] = effort;
        }

        if (interactive || named)
        {
            store.Save(repoRoot, new RememberedChoices(
                agentOptions.GetValueOrDefault("agent") ?? remembered?.Agent,
                agentOptions.GetValueOrDefault("model") ?? (agentOptions.ContainsKey("agent") ? null : remembered?.Model),
                agentOptions.GetValueOrDefault("effort") ?? (agentOptions.ContainsKey("agent") ? null : remembered?.Effort),
                agentOptions.TryGetValue("jobs", out var jobsText) ? int.Parse(jobsText, CultureInfo.InvariantCulture) : remembered?.Jobs,
                [.. steps.Select(AutoSteps.Name)]));
        }

        List<string> Agent(string verb)
        {
            List<string> args = [verb, .. agentOptions.SelectMany(o => new[] { o.Key == "jobs" ? "-j" : "--" + o.Key, o.Value }), "--yes"];
            if (command.Options.TryGetValue("path", out var path)) args.AddRange(["--path", path]);
            return args;
        }

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"== {AutoSteps.Name(step)}");
            if (step == AutoStep.Verify && steps.Contains(AutoStep.Run))
            {
                Console.WriteLine("findings were verified during run");
                continue;
            }

            if (step == AutoStep.Estimate)
            {
                var gate = await EstimateGateAsync(command, repoRoot, fileSystem, interactive, steps, cancellationToken);
                if (gate is { } stop) return stop;
                continue;
            }

            List<string> stepArgs = step switch
            {
                AutoStep.Doctor => ["doctor"],
                AutoStep.Scan => ["scan"],
                AutoStep.Run => [.. Agent("run"), .. steps.Contains(AutoStep.Verify) ? Array.Empty<string>() : ["--no-verify"]],
                AutoStep.Verify => Agent("verify"),
                AutoStep.Report => ["report"],
                AutoStep.Fix => Agent("fix"),
                _ => ["validate"],
            };
            var exit = await RunAsync(CommandLine.Parse([.. stepArgs]), cancellationToken);
            if (exit != 0)
            {
                Console.Error.WriteLine($"stopped: {AutoSteps.Name(step)} exited with {exit}");
                return exit;
            }
        }

        return 0;
    }

    private static int AutoMistake(string problem)
    {
        Console.Error.WriteLine($"codemuster auto: {problem}");
        Console.Error.WriteLine(HelpText.UsageLine("auto"));
        return 2;
    }

    // Prints the estimate; stops when it is above --max-cost, or when the person at the terminal says no, but only if an agent step follows.
    private static async Task<int?> EstimateGateAsync(Command command, string repoRoot, IFileSystem fileSystem, bool interactive, IReadOnlyList<AutoStep> steps, CancellationToken cancellationToken)
    {
        var config = await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken);
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), cancellationToken);
        var report = await new Estimate(ledger, config, command.Options.GetValueOrDefault("path")).RunAsync(cancellationToken);
        Console.WriteLine(report.Render());
        var later = steps.SkipWhile(step => step != AutoStep.Estimate).Skip(1).ToList();
        if (!AutoSteps.NeedsAgent(later)) return null;

        var cost = report.PerCall?.Lines.Sum(line => line.CostUsd) ?? (report.Costs.Count == 0 ? 0 : report.Costs.Max(c => c.CostUsd));
        if (command.Options.TryGetValue("max-cost", out var limitText) && cost > decimal.Parse(limitText, NumberStyles.Number, CultureInfo.InvariantCulture))
        {
            Console.Error.WriteLine($"error: the estimate (~{Spend.Money(cost)}) exceeds --max-cost {Spend.Money(decimal.Parse(limitText, NumberStyles.Number, CultureInfo.InvariantCulture))}; nothing was run");
            return 1;
        }

        if (!interactive) return null;
        Console.Write($"Continue with {string.Join(", ", later.Select(AutoSteps.Name))}? [Y/n] ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is "n" or "no")
        {
            Console.WriteLine("stopped after the estimate; nothing was run");
            return 0;
        }

        return null;
    }

    private static async Task<int> ConfirmedFindingsAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repoRoot, ".codemuster", "ledger.db");
        if (!File.Exists(path)) return 0;
        using var ledger = await SqliteLedger.OpenAsync(path, cancellationToken);
        return (await ledger.GetCurrentFindingsAsync(cancellationToken)).Count(f => f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State != FixState.Fixed);
    }

    // A person is at a terminal (D84): nothing is redirected, CI is unset, and --yes did not ask for no questions.
    private static bool AtTerminal(string[] args) =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) && !args.Contains("--yes");

    // Only reached at a terminal, since parsing requires --agent everywhere else (D84); the answers are remembered for this repository (D86).
    private static Command AskForAgent(Command command, string repoRoot)
    {
        var store = new ChoiceStore(StateDirectory());
        var remembered = store.Load(repoRoot);
        var answers = new AgentQuestions(Console.In, Console.Out).Ask(AgentAdapters.Installed(), remembered, command.Options);
        store.Save(repoRoot, new RememberedChoices(answers.Agent, answers.Model, answers.Effort, answers.Jobs, remembered?.Steps));
        var options = new Dictionary<string, string>(command.Options, StringComparer.Ordinal)
        {
            ["agent"] = answers.Agent,
            ["jobs"] = answers.Jobs.ToString(CultureInfo.InvariantCulture),
        };
        if (answers.Model is { } model) options["model"] = model;
        if (answers.Effort is { } effort) options["effort"] = effort;
        Console.WriteLine($"next time, skip these questions with: codemuster {command.Verb} {string.Join(' ', options.Where(o => o.Key is "agent" or "model" or "effort" or "jobs").Select(o => o.Key == "jobs" ? "-j " + o.Value : $"--{o.Key} {o.Value}"))}");
        return command with { Options = options };
    }

    // Where remembered choices live (D86); CODEMUSTER_STATE_DIR moves it, which the CLI tests use.
    private static string StateDirectory() =>
        Environment.GetEnvironmentVariable("CODEMUSTER_STATE_DIR") is { Length: > 0 } state ? state : Path.Combine(HomeDirectory(), ".codemuster");

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
