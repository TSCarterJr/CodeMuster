using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class PlantedProgramTests
{
    [Fact]
    public async Task A_git_program_in_the_working_directory_never_runs()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        var marker = Plant(repo.Root, "git");

        var status = await CliProcess.RunAsync(repo.Root, "status");

        Assert.False(File.Exists(marker), "the planted git ran");
        Assert.Equal(2, status.ExitCode);
        Assert.Contains("not set up here; run `codemuster init`", status.Stderr);
    }

    [Fact]
    public async Task Doctor_never_runs_a_git_or_node_program_in_the_working_directory()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        var gitMarker = Plant(repo.Root, "git");
        var nodeMarker = Plant(repo.Root, "node");

        var doctor = await CliProcess.RunAsync(repo.Root, "doctor");

        Assert.False(File.Exists(gitMarker), "the planted git ran");
        Assert.False(File.Exists(nodeMarker), "the planted node ran");
        var output = doctor.Stdout.ReplaceLineEndings("\n");
        Assert.StartsWith("git: working\n", output);
        Assert.Matches(new Regex(@"^typescript: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", RegexOptions.Multiline), output);
    }

    /// <summary>Puts a program named <paramref name="name"/> in <paramref name="directory"/>. On Windows it is a copy of whoami.exe, which fails every git or node command line; elsewhere a shell script that leaves the returned marker file.</summary>
    private static string Plant(string directory, string name)
    {
        var marker = Path.Combine(directory, "planted-" + name + "-ran");
        if (OperatingSystem.IsWindows())
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(directory, name + ".exe"));
            return marker;
        }

        var script = Path.Combine(directory, name);
        File.WriteAllText(script, $"#!/bin/sh\necho ran > '{marker}'\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return marker;
    }
}
