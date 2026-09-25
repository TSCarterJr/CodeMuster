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

    private static async Task<TempRepo> ScannedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        return repo;
    }
}
