using System.Text;
using System.Text.Json;

namespace CodeMuster.Cli.Tests;

public class EngineControlTests
{
    // Reads the complete lines written so far, while the command may still be appending to the file.
    private static List<JsonElement> ReadSoFar(string path)
    {
        if (!File.Exists(path)) return [];
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);
        var text = reader.ReadToEnd();
        var complete = text[..(text.LastIndexOf('\n') + 1)];
        return complete.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
    }

    private static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    private static bool Applied(JsonElement e, string command) => Type(e) == "command_applied" && e.GetProperty("command").GetString() == command;

    private static async Task WaitForAsync(string events, Func<JsonElement, bool> seen, Task<CliResult> run, CancellationToken cancellationToken)
    {
        while (!ReadSoFar(events).Any(seen))
        {
            if (run.IsCompleted) Assert.Fail("the command ended first: " + (await run).Stdout + (await run).Stderr);
            await Task.Delay(20, cancellationToken);
        }
    }

    private static void Send(string control, string line)
    {
        using var file = new FileStream(control, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        file.Write(Encoding.UTF8.GetBytes(line + "\n"));
    }

    private static async Task<(TempRepo Repo, int Units)> ScannedAsync()
    {
        var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        var scan = await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file");
        Assert.Equal(0, scan.ExitCode);
        var units = int.Parse(System.Text.RegularExpressions.Regex.Match(scan.Stdout, @"(\d+) total units").Groups[1].Value);
        return (repo, units);
    }

    [Fact]
    public async Task PauseWorkersAndResume_WrittenWhileRunWorks_AreAppliedAndEveryUnitCompletes()
    {
        var (repo, units) = await ScannedAsync();
        using var _ = repo;
        var folder = Path.Combine(Path.GetTempPath(), "codemuster-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var events = Path.Combine(folder, "events.jsonl");
        var control = Path.Combine(folder, "control.txt");
        using var guard = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var run = CliProcess.RunAsync(repo.Root, new Dictionary<string, string>
            {
                ["CODEMUSTER_ENGINE_EVENTS"] = events,
                ["CODEMUSTER_ENGINE_CONTROL"] = control,
                ["CODEMUSTER_FAKE_DELAY_MS"] = "300",
            }, "run", "--agent", "fake", "-j", "1");

            await WaitForAsync(events, e => Type(e) == "unit_started", run, guard.Token);
            Send(control, "pause");
            await WaitForAsync(events, e => Applied(e, "pause"), run, guard.Token);
            Send(control, "workers 2");
            await WaitForAsync(events, e => Applied(e, "workers"), run, guard.Token);
            Send(control, "resume");
            var result = await run.WaitAsync(guard.Token);

            Assert.Equal(0, result.ExitCode);
            var all = ReadSoFar(events);
            Assert.All(all, e => Assert.Equal(1, e.GetProperty("v").GetInt32()));
            Assert.Equal(["pause", "workers", "resume"], all.Where(e => Type(e) == "command_applied").Select(e => e.GetProperty("command").GetString()));
            Assert.DoesNotContain(all, e => Type(e) == "command_rejected");
            Assert.Equal(2, all.Single(e => Applied(e, "workers")).GetProperty("value").GetInt32());
            Assert.Single(all, e => Type(e) == "paused");
            Assert.Single(all, e => Type(e) == "resumed");
            var started = all.Where(e => Type(e) == "unit_started").ToList();
            Assert.Equal(units, started.Count);
            Assert.Equal(started.Select(e => e.GetProperty("unit").GetString()).Order(), all.Where(e => Type(e) == "unit_finished").Select(e => e.GetProperty("unit").GetString()).Order());
            Assert.Contains(started, e => e.GetProperty("worker").GetInt32() == 2);
            var resumedAt = all.FindIndex(e => Type(e) == "resumed");
            Assert.All(started.Where(e => e.GetProperty("worker").GetInt32() == 2), e => Assert.True(all.IndexOf(e) > resumedAt));
            Assert.Equal("run_summary", Type(all[^1]));
            Assert.Equal(units, all[^1].GetProperty("completed").GetInt32());
            Assert.Contains($"completed {units} unit(s), 0 gave up", result.Stdout);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Stop_EndsTheRunAsCtrlCWould()
    {
        var (repo, units) = await ScannedAsync();
        using var _ = repo;
        var folder = Path.Combine(Path.GetTempPath(), "codemuster-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var events = Path.Combine(folder, "events.jsonl");
        var control = Path.Combine(folder, "control.txt");
        using var guard = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var run = CliProcess.RunAsync(repo.Root, new Dictionary<string, string>
            {
                ["CODEMUSTER_ENGINE_EVENTS"] = events,
                ["CODEMUSTER_ENGINE_CONTROL"] = control,
                // Long enough that the run cannot finish every unit before a slow CI runner reads the stop.
                ["CODEMUSTER_FAKE_DELAY_MS"] = "1500",
            }, "run", "--agent", "fake", "-j", "2");

            await WaitForAsync(events, e => Type(e) == "unit_finished", run, guard.Token);
            Send(control, "stop");
            var result = await run.WaitAsync(guard.Token);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("cancelled after", result.Stdout);
            var all = ReadSoFar(events);
            Assert.Single(all, e => Applied(e, "stop"));
            Assert.Equal("run_summary", Type(all[^1]));
            Assert.True(all[^1].GetProperty("cancelled").GetBoolean());
            Assert.True(all[^1].GetProperty("completed").GetInt32() < units);
            var status = await CliProcess.RunAsync(repo.Root, "status");
            Assert.Equal(0, status.ExitCode);
            var again = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "2");
            Assert.Equal(0, again.ExitCode);
            Assert.Contains($"completed {units - all[^1].GetProperty("completed").GetInt32()} unit(s), 0 gave up", again.Stdout);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
