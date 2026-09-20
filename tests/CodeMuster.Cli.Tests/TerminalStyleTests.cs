using CodeMuster.Application;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class TerminalStyleTests
{
    [Theory]
    [InlineData(false, false, "xterm-256color", null, true, true)]
    [InlineData(false, false, "xterm", "1", true, false)]
    [InlineData(false, false, "xterm", "", true, true)]
    [InlineData(true, false, "xterm", null, false, false)]
    [InlineData(false, true, "xterm", null, false, false)]
    [InlineData(false, false, "dumb", null, false, false)]
    public void Capabilities_RespectRedirectionCiAndNoColor(bool redirected, bool ci, string term, string? noColor, bool enabled, bool color)
    {
        var style = TerminalStyle.Detect(redirected, ci, term, noColor);
        Assert.Equal(enabled, style.Enabled);
        Assert.Equal(color, style.Color);
        Assert.Equal(color ? "\u001b[1;36mCODEX\u001b[0m" : "CODEX", style.Accent("CODEX"));
    }

    [Theory]
    [InlineData(DoneOutcome.Recorded, "32")]
    [InlineData(DoneOutcome.Skipped, "33")]
    [InlineData(DoneOutcome.Rejected, "31")]
    [InlineData(DoneOutcome.InvalidResponse, "31")]
    public void CompletedUnits_HaveRealCountBarAndDistinctOutcomes(DoneOutcome outcome, string color)
    {
        using var output = new StringWriter();
        new RunProgressWriter(output, new TerminalStyle(true, true)).Report(
            new RunProgress("file:a.cs", UnitKind.File, "a.cs", 1, outcome, "result", 3, 10));
        Assert.Contains("[###-------]", output.ToString());
        Assert.Contains("3/10", output.ToString());
        Assert.Contains($"\u001b[{color}m", output.ToString());
        Assert.EndsWith("\u001b[0m" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void PlainProgress_HasNoEscapes()
    {
        using var output = new StringWriter();
        new ProgressWriter(output, new TerminalStyle(true, false)).Report("waiting for codex");
        Assert.Contains("waiting for codex", output.ToString());
        Assert.DoesNotContain("\u001b", output.ToString());
    }
}
