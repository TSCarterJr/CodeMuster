namespace CodeMuster.Cli.Tests;

public class ChangeWarningTests
{
    private const string Warning = "since the last scan";

    [Fact]
    public async Task Status_ignores_untracked_files_and_nested_repositories_and_names_the_reviewed_file_that_changed()
    {
        using var repo = await ScannedAsync();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "report", "--out", "audit.md")).ExitCode);
        File.WriteAllText(Path.Combine(repo.Root, "notes.txt"), "scratch\n");
        Directory.CreateDirectory(Path.Combine(repo.Root, "vendorlib"));
        File.WriteAllText(Path.Combine(repo.Root, "vendorlib", "lib.cs"), "class L {}\n");
        repo.Git("-C", "vendorlib", "init", "-q");
        repo.Git("worktree", "add", "-q", "-b", "feature", ".claude/worktrees/feature");
        File.AppendAllText(Path.Combine(repo.Root, "web", "package-lock.json"), "\n");

        var quiet = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, quiet.ExitCode);
        Assert.DoesNotContain(Warning, quiet.Stderr);

        File.AppendAllText(Path.Combine(repo.Root, "web", "lib", "index.ts"), "\nexport const added = 1;\n");
        var warned = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, warned.ExitCode);
        Assert.Equal("web/lib/index.ts changed since the last scan; run codemuster scan to refresh coverage", warned.Stderr.Trim());

        foreach (var path in new[] { "web/lib/api.ts", "web/hooks/useQuotes.ts", "src/MixedRepo.Api/Program.cs", "src/MixedRepo.Api/Data/Quote.cs" })
        {
            File.AppendAllText(Path.Combine(repo.Root, path), "\n// edited\n");
        }

        var many = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(
            "5 files changed since the last scan, including src/MixedRepo.Api/Data/Quote.cs, src/MixedRepo.Api/Program.cs, web/hooks/useQuotes.ts; run codemuster scan to refresh coverage",
            many.Stderr.Trim());
    }

    [Fact]
    public async Task Concurrent_hooks_all_succeed_and_leave_no_temporary_files()
    {
        using var repo = await ScannedAsync();

        var hooks = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => CliProcess.RunAsync(repo.Root, "hook")));

        Assert.All(hooks, hook => Assert.Equal(0, hook.ExitCode));
        Assert.All(hooks, hook => Assert.Equal("{}", hook.Stdout.Trim()));
        Assert.All(hooks, hook => Assert.Equal("", hook.Stderr));
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(repo.Root, ".git", "codemuster")), path => Path.GetFileName(path).StartsWith("changed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hook_that_cannot_record_the_change_warns_and_still_exits_0()
    {
        using var repo = await ScannedAsync();
        var folder = Path.Combine(repo.Root, ".git", "codemuster");
        Directory.Delete(folder, recursive: true);
        File.WriteAllText(folder, "a file where the folder belongs");

        var hook = await CliProcess.RunAsync(repo.Root, "hook");

        Assert.Equal(0, hook.ExitCode);
        Assert.Equal("{}", hook.Stdout.Trim());
        Assert.StartsWith("warning: ", hook.Stderr);
    }

    [Fact]
    public async Task Status_names_the_project_files_scan_loads_and_skips_generated_files_scan_skips()
    {
        using var repo = await ScannedAsync(repo =>
        {
            File.WriteAllText(Path.Combine(repo.Root, ".gitattributes"), "web/lib/api.ts linguist-generated=true\n");
            repo.Git("add", ".gitattributes");
            repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "generated");
        });
        File.AppendAllText(Path.Combine(repo.Root, "web", "lib", "api.ts"), "\n// regenerated\n");

        var quiet = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, quiet.ExitCode);
        Assert.DoesNotContain(Warning, quiet.Stderr);

        File.AppendAllText(Path.Combine(repo.Root, "web", "tsconfig.json"), "\n");
        File.AppendAllText(Path.Combine(repo.Root, "src", "MixedRepo.Api", "MixedRepo.Api.csproj"), "\n");
        var warned = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal("src/MixedRepo.Api/MixedRepo.Api.csproj, web/tsconfig.json changed since the last scan; run codemuster scan to refresh coverage", warned.Stderr.Trim());
    }

    [Fact]
    public async Task Next_and_run_warn_on_stderr_before_working_from_a_stale_map()
    {
        using var repo = await ScannedAsync();
        File.AppendAllText(Path.Combine(repo.Root, "web", "lib", "index.ts"), "\nexport const added = 1;\n");
        const string expected = "web/lib/index.ts changed since the last scan; run codemuster scan to refresh coverage";

        var next = await CliProcess.RunAsync(repo.Root, "next");
        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--kind", "file", "--path", "web/lib/index.ts");

        Assert.Equal(0, next.ExitCode);
        Assert.StartsWith(expected, next.Stderr);
        Assert.StartsWith("# CodeMuster unit", next.Stdout);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expected, run.Stderr);
    }

    [Fact]
    public async Task A_changed_file_git_cannot_read_turns_the_change_check_into_a_warning_and_status_still_runs()
    {
        using var repo = await ScannedAsync();
        var path = Path.Combine(repo.Root, "web", "lib", "index.ts");
        File.AppendAllText(path, "\nexport const added = 1;\n");

        CliResult status;
        if (OperatingSystem.IsWindows())
        {
            // Some Windows scanners and editors hold a file with no sharing, so git cannot open it.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                status = await CliProcess.RunAsync(repo.Root, "status");
            }
        }
        else
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.None);
            try
            {
                status = await CliProcess.RunAsync(repo.Root, "status");
            }
            finally
            {
                File.SetUnixFileMode(path, mode);
            }
        }

        Assert.Equal(0, status.ExitCode);
        Assert.StartsWith("warning: could not check for changes since the last scan: ", status.Stderr);
        Assert.Contains("analyzed", status.Stdout);
    }

    private static async Task<TempRepo> ScannedAsync(Action<TempRepo>? prepare = null)
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        prepare?.Invoke(repo);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        return repo;
    }
}
