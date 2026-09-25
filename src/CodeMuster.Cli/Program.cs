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
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Command command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(ex.Usage);
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
            return await DoctorAsync(fileSystem, cancellationToken);
        }

        if (command.Verb == "hook")
        {
            return await HookAsync(fileSystem, cancellationToken);
        }

        var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        var tree = new GitSourceTree(repoRoot);
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
        var changes = ChangeTracker(repoRoot, config);
        if (command.Verb is "status" or "report" or "fix" or "verify" && await changes.ChangesAsync(cancellationToken) is { Detected: true } changed)
        {
            Console.Error.WriteLine(ChangeWarning(changed.Paths));
        }

        switch (command.Verb)
        {
            case "scan":
                var snapshot = await changes.SnapshotAsync(cancellationToken);
                var exit = await ScanAsync(command, repoRoot, ledger, tree, clock, config, cancellationToken);
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
                var response = await File.ReadAllTextAsync(command.Options["findings"], cancellationToken);
                var done = await new Done(ledger, clock, config, fileSystem: fileSystem, repoRoot: repoRoot, tree: tree, hasher: new GitBlobHasher(repoRoot)).RunAsync(command.Positionals[0], command.Options["fingerprint"], response, cancellationToken);
                Console.WriteLine(done.Message);
                return done.Outcome == DoneOutcome.Recorded ? 0 : 1;
            case "run":
            case "verify":
                return await RunAgentAsync(command, repoRoot, ledger, tree, clock, config, cancellationToken);
            case "validate":
                ITestRunner? runner = config.TestCommand.Count == 0 ? null : new CommandTestRunner(repoRoot, config.TestCommand);
                var validation = await new Validate(runner).RunAsync(cancellationToken);
                Console.WriteLine(validation.Output);
                Console.WriteLine(validation.Passed ? "validation passed" : "validation failed");
                return validation.Passed ? 0 : 1;
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
        var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), clock, config, mappers, repoRoot, new ProgressWriter(Console.Error), new DependencyAuditor())
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

    private static async Task<int> FixAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, CancellationToken cancellationToken)
    {
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
        };
        if (options.RelatedFiles.Count > 0)
        {
            var tracked = (await tree.ListFilesAsync(cancellationToken)).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            if (options.Parallelism != 1 || options.Path is null || !tracked.Contains(options.Path) || options.RelatedFiles.Any(p => !tracked.Contains(p)))
                throw new ArgumentException("--include-related requires -j 1, an exact tracked --path, and exact existing tracked related files");
        }
        await PreviewAgentAsync(adapter.Identity, options.Parallelism, clock, cancellationToken);
        var concurrency = options.Parallelism == 1 ? "one file at a time" : string.Create(CultureInfo.InvariantCulture, $"up to {options.Parallelism} files at a time");
        Console.WriteLine($"fixing with {command.Options["agent"]}, {concurrency}; each file it changes becomes a commit");
        ITestRunner? tests = config.TestCommand.Count > 0 ? new CommandTestRunner(repoRoot, config.TestCommand) : null;
        if (tests is null)
        {
            Console.Error.WriteLine("warning: no test_command in .codemuster/config.json, so nothing checks that a fix still builds");
        }

        var progress = new ProgressWriter(Console.Out, ConsoleStyle());
        using var fileFixer = new GitFileFixer(repoRoot, directory => AgentAdapters.Create(
            identity.Agent, null, identity.Model, identity.Effort, write: true, workingDirectory: directory), progress, options.RelatedFiles);
        var fix = new Fix(ledger, tree, clock, config, workspace, tests, fileFixer, new GitBlobHasher(repoRoot));
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

    private static async Task<int> HookAsync(PhysicalFileSystem fileSystem, CancellationToken cancellationToken)
    {
        try
        {
            var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
            if (fileSystem.FileExists(ConfigLoader.PathFor(repoRoot)))
            {
                var config = await new ConfigLoader(fileSystem).LoadAsync(repoRoot, cancellationToken);
                await ChangeTracker(repoRoot, config).NotifyAsync(cancellationToken);
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
        new(repoRoot, path => config.ExcludedReason(path, linguistGenerated: false) is null);

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

    private static IReadOnlyList<ICodeMapper> Mappers() => [new RoslynMapper(), new TypeScriptMapper(() => ExecutableResolver.Resolve("node"))];

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
            await File.WriteAllTextAsync(outPath, text, cancellationToken);
            Console.WriteLine($"wrote {packs.Count} pack(s) to {outPath}");
        }
        else
        {
            Console.Write(text);
        }

        return 0;
    }

    private static async Task<int> RunAgentAsync(Command command, string repoRoot, SqliteLedger ledger, GitSourceTree tree, SystemClock clock, Config config, CancellationToken cancellationToken)
    {
        var template = Environment.GetEnvironmentVariable("CODEMUSTER_FAKE_RESPONSE");
        var identity = await ResolveAgentAsync(command, repoRoot, cancellationToken);
        var adapter = AgentAdapters.Create(
            identity.Agent,
            template is null ? null : await File.ReadAllTextAsync(template, cancellationToken),
            identity.Model, identity.Effort, workingDirectory: repoRoot);
        var kind = command.Verb == "verify" ? "verify" : command.Options.GetValueOrDefault("kind");
        var options = new RunOptions(
            int.Parse(command.Options.GetValueOrDefault("jobs", "1")),
            int.Parse(command.Options.GetValueOrDefault("attempts", "3")),
            command.Flags.Contains("force"),
            kind is null ? null : Enum.Parse<UnitKind>(kind, ignoreCase: true),
            command.Options.GetValueOrDefault("path"));
        await PreviewAgentAsync(adapter.Identity, options.Parallelism, clock, cancellationToken);
        if (kind == "verify") await new RefreshVerification(ledger, tree, config, new GitBlobHasher(repoRoot)).RunAsync(cancellationToken);
        Console.WriteLine($"running {command.Options["agent"]} on up to {options.Parallelism} unit(s) at a time; a line prints as each unit finishes");
        var result = await new Run(ledger, tree, clock, config, adapter, new RunProgressWriter(Console.Out, ConsoleStyle()), new ProgressWriter(Console.Out, ConsoleStyle())).RunAsync(options, cancellationToken);
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

    private static Task PreviewAgentAsync(AgentIdentity identity, int parallelism, IClock clock, CancellationToken cancellationToken)
    {
        var style = ConsoleStyle();
        var interactive = style.Enabled && !Console.IsInputRedirected;
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
            Console.Write("install skills and hooks for which agents? [claude,codex,gemini / none; default all] ");
            selection = Console.ReadLine();
            selection = string.IsNullOrWhiteSpace(selection) ? "all" : selection.Trim();
        }
        selection ??= command.Flags.Contains("yes") ? "all" : "none";
        var agents = command.Flags.Contains("no-skills") || selection == "none" ? Array.Empty<string>() : AgentSetup.Select(selection);
        await new AgentSetup(fileSystem).InstallAsync(repoRoot, agents, !command.Flags.Contains("no-hooks"), EmbeddedSkill.Text, cancellationToken);
        foreach (var agent in agents) Console.WriteLine($"installed {agent} skill" + (command.Flags.Contains("no-hooks") ? "" : " and change hook (reload the agent and approve hooks if prompted)"));
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

    private static string Version =>
        (typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
