using System.Globalization;
using System.Text.Json;

namespace CodeMuster.Cli.Tests;

public class EngineEventsTests
{
    internal static List<JsonElement> ReadEvents(string path)
    {
        var text = File.ReadAllText(path);
        Assert.EndsWith("\n", text);
        Assert.DoesNotContain('\r', text);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
    }

    private static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    [Fact]
    public async Task ScanAndRun_WithTheEventsVariable_WriteVersionedJsonLines_PairedUnits_AndASummaryLast()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        repo.WithoutVulnerabilityScan();
        var folder = Path.Combine(Path.GetTempPath(), "codemuster-engine-" + Guid.NewGuid().ToString("N"));
        var scanEvents = Path.Combine(folder, "scan.jsonl");
        var runEvents = Path.Combine(folder, "run.jsonl");
        try
        {
            var scan = await CliProcess.RunAsync(repo.Root, new Dictionary<string, string> { ["CODEMUSTER_ENGINE_EVENTS"] = scanEvents }, "scan", "--mode", "file");
            Assert.Equal(0, scan.ExitCode);
            var scanned = ReadEvents(scanEvents);
            Assert.All(scanned, e => Assert.Equal(1, e.GetProperty("v").GetInt32()));
            Assert.All(scanned, e => Assert.EndsWith("Z", e.GetProperty("t").GetString()));
            Assert.All(scanned, e => DateTimeOffset.Parse(e.GetProperty("t").GetString()!, CultureInfo.InvariantCulture));
            Assert.Contains(scanned, e => Type(e) == "scan_progress" && e.GetProperty("message").GetString() == "planning units");
            Assert.Equal("scan_summary", Type(scanned[^1]));
            var units = scanned[^1].GetProperty("units_total").GetInt32();
            Assert.True(units > 0);

            var run = await CliProcess.RunAsync(repo.Root, new Dictionary<string, string> { ["CODEMUSTER_ENGINE_EVENTS"] = runEvents }, "run", "--agent", "fake", "-j", "3");
            Assert.Equal(0, run.ExitCode);
            var events = ReadEvents(runEvents);
            Assert.All(events, e => Assert.Equal(1, e.GetProperty("v").GetInt32()));
            Assert.Equal("run_started", Type(events[0]));
            Assert.Equal((3, "fake"), (events[0].GetProperty("workers").GetInt32(), events[0].GetProperty("agent").GetString()));
            Assert.Equal("run_summary", Type(events[^1]));
            Assert.False(events[^1].GetProperty("cancelled").GetBoolean());
            var started = events.Where(e => Type(e) == "unit_started").Select(e => (e.GetProperty("unit").GetString(), e.GetProperty("attempt").GetInt32())).ToList();
            var finished = events.Where(e => Type(e) == "unit_finished").Select(e => (e.GetProperty("unit").GetString(), e.GetProperty("attempt").GetInt32())).ToList();
            Assert.Equal(started.Order(), finished.Order());
            Assert.Equal(started.Count, started.Distinct().Count());
            Assert.Equal(units, started.Count);
            Assert.Equal(started.Count, events[^1].GetProperty("completed").GetInt32());
            Assert.All(events.Where(e => Type(e) == "unit_started"), e => Assert.InRange(e.GetProperty("worker").GetInt32(), 1, 3));
            Assert.All(events.Where(e => Type(e) == "unit_finished"), e =>
            {
                Assert.Equal("recorded", e.GetProperty("outcome").GetString());
                Assert.Equal(1200, e.GetProperty("usage").GetProperty("input_tokens").GetInt64());
            });
            Assert.Contains($"completed {units} unit(s)", run.Stdout);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutTheVariable_NoEventsAreWritten_AndHelpNeverMentionsTheEngine()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills");
        repo.WithoutVulnerabilityScan();

        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);

        Assert.Empty(Directory.GetFiles(repo.Root, "*.jsonl", SearchOption.AllDirectories));
        foreach (var command in new[] { "--help", "help run", "help verify", "help fix", "help scan" })
        {
            var help = await CliProcess.RunAsync(repo.Root, command.Split(' '));
            Assert.DoesNotContain("ENGINE", help.Stdout, StringComparison.OrdinalIgnoreCase);
        }
    }
}
