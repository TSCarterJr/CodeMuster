using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class AgentActivityTests
{
    [Fact]
    public async Task Activity_AnimatesWithElapsedTimeAndStopsBeforeCompletionLine()
    {
        var clock = new Clock();
        using var output = new StringWriter();
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = 0;
        var activity = new AgentActivity(output, clock, new TerminalStyle(true, true), (duration, token) =>
        {
            if (++delays == 1)
            {
                clock.UtcNow += TimeSpan.FromSeconds(3);
                return Task.CompletedTask;
            }
            ticked.TrySetResult();
            return Task.Delay(Timeout.Infinite, token);
        });
        var result = await activity.RunAsync("Inspecting repository", async token =>
        {
            await ticked.Task.WaitAsync(token);
            return 42;
        }, CancellationToken.None);
        Assert.Equal(42, result);
        Assert.Contains("| 0s Inspecting repository", output.ToString());
        Assert.Contains("/ 3s Inspecting repository", output.ToString());
        Assert.Contains("3s", output.ToString());
        Assert.Contains("[ok] 3s Inspecting repository", output.ToString());
        Assert.EndsWith("\u001b[0m" + Environment.NewLine, output.ToString());
        Assert.Equal(2, delays);
    }

    [Fact]
    public async Task RedirectedActivity_OnlyRunsOperation()
    {
        using var output = new StringWriter();
        var activity = new AgentActivity(output, new Clock(), new TerminalStyle(false, false),
            (_, _) => throw new InvalidOperationException("must not animate"));
        Assert.Equal(7, await activity.RunAsync("Inspecting", _ => Task.FromResult(7), CancellationToken.None));
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "cancelled")]
    public async Task FailureOrCancellation_CleansUpWithoutClaimingSuccess(bool cancel, string expected)
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var activity = new AgentActivity(output, new Clock(), new TerminalStyle(true, false), Task.Delay);
        await Assert.ThrowsAnyAsync<Exception>(() => activity.RunAsync<int>("Inspecting", token =>
        {
            if (!cancel) throw new InvalidOperationException("agent failed");
            cancellation.Cancel();
            return Task.FromCanceled<int>(token);
        }, cancellation.Token));
        Assert.Contains(expected, output.ToString());
        Assert.DoesNotContain("[ok]", output.ToString());
        Assert.DoesNotContain("\u001b", output.ToString());
        Assert.EndsWith(Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task NarrowTerminal_DoesNotWrapAnimation()
    {
        using var output = new StringWriter();
        var activity = new AgentActivity(output, new Clock(), new TerminalStyle(true, false, 30), Task.Delay);
        await activity.RunAsync("Inspecting a repository with a long name", _ => Task.FromResult(0), CancellationToken.None);
        Assert.All(output.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), line => Assert.True(line.Length < 30, line));
        Assert.Contains("0s", output.ToString());
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
    }
}
