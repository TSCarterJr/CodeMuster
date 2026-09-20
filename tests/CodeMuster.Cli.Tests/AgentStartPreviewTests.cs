using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class AgentStartPreviewTests
{
    [Fact]
    public async Task Countdown_PrintsSettingsAndStartsAfterTenSeconds()
    {
        var clock = new ManualClock();
        var start = clock.UtcNow;
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, clock, () => null, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.UtcNow += duration;
            return Task.CompletedTask;
        });

        await preview.RunAsync(new AgentIdentity("codex", "astra", "xhigh"), 50, true, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(10), clock.UtcNow - start);
        Assert.Contains("CODEMUSTER", output.ToString());
        Assert.Contains("Provider  CODEX", output.ToString());
        Assert.Contains("Model     astra", output.ToString());
        Assert.Contains("Thinking  xhigh", output.ToString());
        Assert.Contains("Workers   up to 50", output.ToString());
        Assert.Contains("Starting in 10s", output.ToString());
        Assert.Contains("Starting in  1s", output.ToString());
        Assert.Contains("Enter: start now", output.ToString());
        Assert.Contains("Esc/Ctrl+C: cancel", output.ToString());
    }

    [Fact]
    public async Task Enter_SkipsTheWait()
    {
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () => ConsoleKey.Enter,
            (_, _) => throw new InvalidOperationException("must start immediately"));

        await preview.RunAsync(new AgentIdentity("codex"), 1, true, CancellationToken.None);

        Assert.Contains("Model     provider default", output.ToString());
        Assert.Contains("Thinking  provider default", output.ToString());
    }

    [Fact]
    public async Task Escape_CancelsBeforeReturningToAgentWork()
    {
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () => ConsoleKey.Escape,
            (_, _) => throw new InvalidOperationException("must cancel immediately"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            preview.RunAsync(new AgentIdentity("codex"), 50, true, CancellationToken.None));
    }

    [Fact]
    public async Task CtrlC_CancelsTheDelay()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () => null, (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            preview.RunAsync(new AgentIdentity("codex"), 50, true, cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_WinsOverEnter()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () =>
        {
            cancellation.Cancel();
            return ConsoleKey.Enter;
        }, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            preview.RunAsync(new AgentIdentity("codex"), 50, true, cancellation.Token));
    }

    [Fact]
    public async Task Noninteractive_PrintsSettingsWithoutReadingOrWaiting()
    {
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(),
            () => throw new InvalidOperationException("must not read input"),
            (_, _) => throw new InvalidOperationException("must not wait"));

        await preview.RunAsync(new AgentIdentity("gemini"), 3, false, CancellationToken.None);

        Assert.Contains("using gemini, Model: provider default, Thinking: not supported", output.ToString());
        Assert.DoesNotContain("Starting in", output.ToString());
    }

    [Fact]
    public async Task UnrelatedKeys_DoNotStartEarly()
    {
        var clock = new ManualClock();
        var start = clock.UtcNow;
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, clock, () => ConsoleKey.A, (duration, _) =>
        {
            clock.UtcNow += duration;
            return Task.CompletedTask;
        });

        await preview.RunAsync(new AgentIdentity("claude", "sonnet", "high"), 2, true, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(10), clock.UtcNow - start);
    }

    [Fact]
    public async Task ColoredPreview_EmphasizesSettingsAndResetsColor()
    {
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () => ConsoleKey.Enter,
            (_, _) => Task.CompletedTask, new TerminalStyle(true, true));
        await preview.RunAsync(new AgentIdentity("codex", "astra", "xhigh"), 50, true, CancellationToken.None);
        Assert.Contains("\u001b[1;36mCODEX\u001b[0m", output.ToString());
        Assert.Contains("\u001b[1mastra\u001b[0m", output.ToString());
        Assert.Contains("\u001b[1;35mxhigh\u001b[0m", output.ToString());
    }

    [Fact]
    public async Task NarrowPreview_KeepsControlsOffAnimatedLine()
    {
        using var output = new StringWriter();
        var preview = new AgentStartPreview(output, new ManualClock(), () => ConsoleKey.Enter,
            (_, _) => Task.CompletedTask, new TerminalStyle(true, false, 32));
        await preview.RunAsync(new AgentIdentity("codex"), 1, true, CancellationToken.None);
        Assert.Contains("Enter: start now", output.ToString());
        Assert.Contains("Esc/Ctrl+C: cancel", output.ToString());
        Assert.DoesNotContain("| Enter", output.ToString());
    }

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-19T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    }
}
