using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class DoctorCommandTests
{
    [Fact]
    public async Task Doctor_IsReadyOnARestoredFixture_AndNamesTheRestoreOnceObjIsGone()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");

        var healthy = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.Equal(0, healthy.ExitCode);
        var lines = healthy.Stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("git: working", lines[0]);
        Assert.Matches(@"^csharp: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", lines[1]);
        Assert.Matches(@"^typescript: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", lines[2]);
        Assert.Equal("ready", lines[3]);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".codemuster")));
        var progress = healthy.Stderr.ReplaceLineEndings("\n");
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] git: listed \d+ files$", RegexOptions.Multiline), progress);
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] csharp: loading MixedRepo\.sln$", RegexOptions.Multiline), progress);
        Assert.Matches(new Regex(@"^\[\s*\d+ s\] typescript: loading web/tsconfig\.json$", RegexOptions.Multiline), progress);

        foreach (var obj in Directory.GetDirectories(Path.Combine(repo.Root, "src"), "obj", SearchOption.AllDirectories))
        {
            Directory.Delete(obj, recursive: true);
        }

        var unrestored = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.Equal(1, unrestored.ExitCode);
        var output = unrestored.Stdout.ReplaceLineEndings("\n");
        Assert.Matches(new Regex(@"^csharp: failed in \d+\.\d s$", RegexOptions.Multiline), output);
        Assert.Contains("  src/MixedRepo.Api/MixedRepo.Api.csproj is not restored; run dotnet restore MixedRepo.sln\n", output);
        Assert.EndsWith("\nnot ready", output.TrimEnd());
    }

    [Fact]
    public async Task DoctorFix_OnAFreshCloneOfTheMixedFixture_OffersTheRestoreAndTheInstallTheMappersName()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");

        var result = await CliProcess.RunAsync(repo.Root, "doctor", "--fix");

        Assert.Equal(1, result.ExitCode);
        var output = result.Stdout.ReplaceLineEndings("\n");
        Assert.Contains("  src/MixedRepo.Api/MixedRepo.Api.csproj is not restored; run dotnet restore MixedRepo.sln\n", output);
        Assert.Contains("typescript was not found for web/tsconfig.json; run npm ci --prefix web\n", output);
        Assert.Contains("would run: dotnet restore MixedRepo.sln\nwould run: cd web && npm ci\n", output);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "web", "node_modules")));
    }

    [Fact]
    public async Task Doctor_AndScan_LeaveLooseJavaScriptWholeFile_WithANote_WhenNoTypescriptPackageIsInstalled()
    {
        const string note = "javascript: not mapped (no tsconfig, jsconfig or typescript package); files are reviewed whole. Add typescript as a dev dependency to map them";
        using var repo = TempRepo.FromFixture("minimal-api");
        File.WriteAllText(Path.Combine(repo.Root, "src", "MinimalApi", "site.js"), "function toggle() {\n  return 1;\n}\n");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "script");
        repo.Dotnet("restore", "MinimalApi.sln");

        var doctor = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.Equal(0, doctor.ExitCode);
        var lines = doctor.Stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal(["git: working", note, "ready"], lines.Where(line => !line.StartsWith("csharp: working", StringComparison.Ordinal)));

        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        var scan = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, scan.ExitCode);
        Assert.Contains(note, scan.Stderr);
        Assert.DoesNotContain("warning:", scan.Stderr);
        using var ledger = await CodeMuster.Infrastructure.SqliteLedger.OpenAsync(Path.Combine(repo.Root, ".codemuster", "ledger.db"), CancellationToken.None);
        var stored = (await ledger.GetCodeMapAsync(CancellationToken.None))!;
        Assert.False(stored.IsPartial, string.Join("\n", stored.Map.Diagnostics));
        Assert.Equal(["csharp"], stored.MappedLanguages);
        Assert.Empty(stored.FailedLanguages);
    }

    [Fact]
    public async Task Doctor_OutsideAGitRepository_SaysGitFailed_AndExits1()
    {
        var folder = Directory.CreateTempSubdirectory("codemuster-doctor-");
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_CEILING_DIRECTORIES"] = folder.Parent!.FullName };

            var result = await CliProcess.RunAsync(folder.FullName, environment, "doctor");

            Assert.Equal(1, result.ExitCode);
            var output = result.Stdout.ReplaceLineEndings("\n");
            Assert.StartsWith("git: failed\n  ", output);
            Assert.Contains("not a git repository", output);
            Assert.EndsWith("\nnot ready", output.TrimEnd());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static Dictionary<string, string> PlainFolderEnvironment(DirectoryInfo folder) => new()
    {
        ["GIT_CEILING_DIRECTORIES"] = folder.Parent!.FullName,
        ["GIT_AUTHOR_NAME"] = "t",
        ["GIT_AUTHOR_EMAIL"] = "t@example.com",
        ["GIT_COMMITTER_NAME"] = "t",
        ["GIT_COMMITTER_EMAIL"] = "t@example.com",
        ["GIT_CONFIG_COUNT"] = "1",
        ["GIT_CONFIG_KEY_0"] = "commit.gpgsign",
        ["GIT_CONFIG_VALUE_0"] = "false",
    };

    [Fact]
    public async Task DoctorFixYes_InAPlainFolder_CreatesTheRepositoryWithAFirstCommit_AndThenReportsGitWorking()
    {
        var folder = Directory.CreateTempSubdirectory("codemuster-doctor-fix-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "notes.txt"), "hello\n");

            var result = await CliProcess.RunAsync(folder.FullName, PlainFolderEnvironment(folder), "doctor", "--fix", "--yes");

            var output = result.Stdout.ReplaceLineEndings("\n");
            Assert.True(result.ExitCode == 0, output + result.Stderr);
            Assert.StartsWith("git: failed\n  ", output);
            Assert.Contains("running: git init && git add -A && git commit -m \"Initial commit\"\n", output);
            Assert.EndsWith("\ngit: working\nready", output.TrimEnd());
            Assert.True(Directory.Exists(Path.Combine(folder.FullName, ".git")));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task DoctorFix_WhenNotATerminal_OnlyPrintsTheCommands_AndChangesNothing()
    {
        var folder = Directory.CreateTempSubdirectory("codemuster-doctor-fix-");
        try
        {
            var result = await CliProcess.RunAsync(folder.FullName, PlainFolderEnvironment(folder), "doctor", "--fix");

            Assert.Equal(1, result.ExitCode);
            var output = result.Stdout.ReplaceLineEndings("\n");
            Assert.Contains("would run: git init && git add -A && git commit -m \"Initial commit\"\n", output);
            Assert.Contains("nothing was run because this is not an interactive terminal", output);
            Assert.False(Directory.Exists(Path.Combine(folder.FullName, ".git")));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    // git marks its object files read-only, which Directory.Delete refuses on Windows.
    private static void DeleteFolder(DirectoryInfo folder)
    {
        foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        folder.Delete(recursive: true);
    }
}
