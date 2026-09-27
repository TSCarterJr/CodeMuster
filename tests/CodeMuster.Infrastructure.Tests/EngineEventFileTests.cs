using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class EngineEventFileTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 27, 10, 0, 0, TimeSpan.FromHours(2));
    }

    private static EngineEvent Event(string type, params (string Name, object? Value)[] fields) =>
        new(type, fields.ToDictionary(f => f.Name, f => f.Value));

    [Fact]
    public void EachEvent_IsOneLfTerminatedJsonLine_WithVersionTimeAndTypeFirst_ThenItsFieldsInOrder()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "events.jsonl");

        using (var stream = new EngineEventFile(path, new FixedClock()))
        {
            stream.Emit(Event("unit_finished",
                ("unit", "file:src/a.cs"), ("attempt", 2), ("duration_ms", 1500L), ("gave_up", false), ("cost_usd", 0.25m), ("message", "café \"quoted\"\nnext"),
                ("usage", new Dictionary<string, object?> { ["input_tokens"] = 100L, ["model"] = null }), ("fixed", new long[] { 3, 4 }), ("rate", 0.5)));
            stream.Emit(Event("run_summary"));
        }

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.Equal(
            """{"v":1,"t":"2026-09-27T08:00:00.0000000Z","type":"unit_finished","unit":"file:src/a.cs","attempt":2,"duration_ms":1500,"gave_up":false,"cost_usd":0.25,"message":"café \"quoted\"\nnext","usage":{"input_tokens":100,"model":null},"fixed":[3,4],"rate":0.5}""" + "\n" +
            """{"v":1,"t":"2026-09-27T08:00:00.0000000Z","type":"run_summary"}""" + "\n",
            text);
    }

    [Fact]
    public void AnExistingFile_IsAppendedTo_AndCanBeReadWhileOpen()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "events.jsonl");
        File.WriteAllText(path, "{\"v\":1,\"type\":\"earlier\"}\n");

        using var stream = new EngineEventFile(path, new FixedClock());
        stream.Emit(Event("later"));
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

        Assert.Equal(["earlier", "later"], reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task ConcurrentEmits_WriteWholeLines()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "nested", "events.jsonl");

        using (var stream = new EngineEventFile(path, new FixedClock()))
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            {
                for (var i = 0; i < 200; i++) stream.Emit(Event("unit_started", ("worker", worker), ("seq", i), ("padding", new string('x', 500))));
            })));
        }

        var lines = File.ReadAllText(path).Split('\n');
        Assert.Equal("", lines[^1]);
        var parsed = lines[..^1].Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal(1600, parsed.Count);
        Assert.All(parsed, e => Assert.Equal(1, e.GetProperty("v").GetInt32()));
        Assert.All(Enumerable.Range(0, 8), worker => Assert.Equal(Enumerable.Range(0, 200),
            parsed.Where(e => e.GetProperty("worker").GetInt32() == worker).Select(e => e.GetProperty("seq").GetInt32())));
    }
}
