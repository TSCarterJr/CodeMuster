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

    [Fact]
    public async Task The_shipped_executable_never_runs_a_dotnet_program_in_the_working_directory()
    {
        using var repo = TempRepo.FromFixture("minimal-api");
        repo.Dotnet("restore", "MinimalApi.sln");
        // Roslyn starts its build host by the bare name dotnet. Run as dotnet codemuster.dll the dotnet install is searched first,
        // so only the executable the release ships shows whether the working directory can supply that program.
        var marker = Plant(repo.Root, "dotnet");

        var doctor = await CliProcess.RunAppHostAsync(repo.Root, "doctor");

        Assert.False(File.Exists(marker), "the planted dotnet ran");
        Assert.Matches(new Regex(@"^csharp: working in \d+\.\d s, [1-9]\d* symbol\(s\)$", RegexOptions.Multiline), doctor.Stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task A_yarn_shim_never_runs_a_node_program_committed_beside_the_manifest()
    {
        using var repo = TempRepo.FromFixture("minimal-api");
        repo.Git("rm", "-r", "-q", "src", "MinimalApi.sln");
        var tools = Path.Combine(repo.Root, "tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "package.json"), "{ \"name\": \"tools\", \"version\": \"1.0.0\" }\n");
        File.WriteAllText(Path.Combine(tools, "yarn.lock"), "# yarn lockfile v1\n");
        var marker = Plant(tools, "node", windowsScript: true);
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "tools");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        var shims = repo.Root + "-shims";
        Directory.CreateDirectory(shims);
        try
        {
            // What npm i -g yarn installs where no node.exe sits beside the shim: on Windows the cmd-shim falls back to a bare node,
            // which cmd.exe looks for in its current directory first; elsewhere a script whose shebang runs env node from PATH.
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path.Combine(shims, "yarn.cmd"),
                    "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n\r\n"
                    + "IF EXIST \"%dp0%\\node.exe\" (\r\n  SET \"_prog=%dp0%\\node.exe\"\r\n) ELSE (\r\n  SET \"_prog=node\"\r\n  SET PATHEXT=%PATHEXT:;.JS;=;%\r\n)\r\n\r\n"
                    + "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\yarn\\bin\\yarn.js\" %*\r\n");
            }
            else
            {
                var yarn = Path.Combine(shims, "yarn");
                File.WriteAllText(yarn, "#!/usr/bin/env node\nconsole.log('1.22.22');\n");
                File.SetUnixFileMode(yarn, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var path = shims + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            var scan = await CliProcess.RunAsync(repo.Root, new Dictionary<string, string> { ["PATH"] = path }, "scan", "--mode", "file");

            Assert.False(File.Exists(marker), "the planted node ran");
            Assert.Contains("auditing tools/package.json with yarn", scan.Stderr);
        }
        finally
        {
            Directory.Delete(shims, recursive: true);
        }
    }

    /// <summary>Puts a program named <paramref name="name"/> in <paramref name="directory"/>. On Windows it is a copy of whoami.exe, which fails every git or node command line; elsewhere a shell script that leaves the returned marker file.</summary>
    private static string Plant(string directory, string name, bool windowsScript = false)
    {
        var marker = Path.Combine(directory, "planted-" + name + "-ran");
        if (OperatingSystem.IsWindows() && windowsScript)
        {
            // cmd.exe finds a bare name through PATHEXT, so a .cmd beside the manifest stands in for a committed node.exe.
            File.WriteAllText(Path.Combine(directory, name + ".cmd"), $"@echo ran> \"{marker}\"\r\n");
            return marker;
        }

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
