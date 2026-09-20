namespace CodeMuster.Cli.Tests;

public class ModelOptionTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("verify")]
    [InlineData("fix")]
    public async Task CodexCommands_ResolveSettingsOnceBeforePreview(string verb)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        var bin = Path.Combine(repo.Root, ".codemuster", "bin");
        Directory.CreateDirectory(bin);
        var script = Path.Combine(bin, "node_modules", "@openai", "codex", "bin", "codex.js");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, """
            #!/usr/bin/env node
            const fs = require('node:fs');
            if (process.argv[2] !== 'app-server') process.exit(7);
            fs.appendFileSync('.codemuster/lookups', 'lookup\n');
            require('node:readline').createInterface({input:process.stdin}).on('line', line => {
              const request = JSON.parse(line);
              if (request.method === 'initialized') return;
              const result = request.method === 'initialize' ? {} : {config:{model:'resolved-model',model_reasoning_effort:'high'}};
              process.stdout.write(JSON.stringify({id:request.id,result})+'\n');
            });
            """);
        if (OperatingSystem.IsWindows()) File.WriteAllText(Path.Combine(bin, "codex.cmd"), "@exit /b 7");
        else
        {
            File.Copy(script, Path.Combine(bin, "codex"));
            File.SetUnixFileMode(Path.Combine(bin, "codex"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var environment = new Dictionary<string, string> { ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") };

        var result = await CliProcess.RunAsync(repo.Root, environment, verb, "--agent", "codex", "-j", "25");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Model: resolved-model, Thinking: high", result.Stderr);
        Assert.Equal("lookup\n", File.ReadAllText(Path.Combine(repo.Root, ".codemuster", "lookups")));
    }

    [Fact]
    public async Task RunWithAModelAndEffort_RecordsThemAndTheReportSaysWhoAudited()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3", "--model", "test-model", "--effort", "low");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Running up to 3 agents, using fake, Model: test-model, Thinking: low", run.Stderr);
        Assert.DoesNotContain("Starting in", run.Stderr);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("audited by fake test-model/low (", report.Stdout);
    }

    [Fact]
    public async Task RunWithoutAModel_SaysOnlyTheAgentName()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "3");
        Assert.Contains("Model: provider default, Thinking: provider default", run.Stderr);

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("audited by fake (", report.Stdout);
        Assert.DoesNotContain("audited by fake /", report.Stdout);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("fix")]
    public async Task AgentCommands_ShowSettingsWithoutWaitingWhenRedirected(string verb)
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");

        var result = await CliProcess.RunAsync(repo.Root, verb, "--agent", "fake", "-j", "50", "--model", "test-model", "--effort", "xhigh");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Running up to 50 agents, using fake, Model: test-model, Thinking: xhigh", result.Stderr);
        Assert.DoesNotContain("Starting in", result.Stderr);
    }
}
