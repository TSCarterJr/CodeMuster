using CodeMuster.Application;
using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class RunProgressWriterTests
{
    [Fact]
    public void SkippedUnit_DoesNotClaimAnAgentAttempt()
    {
        var writer = new StringWriter { NewLine = "\n" };
        new RunProgressWriter(writer).Report(new RunProgress("file:large.cs", UnitKind.File,
            "large.cs", 0, DoneOutcome.Skipped, "skipped: too large", 1, 2));
        Assert.Equal("1/2 file large.cs: skipped: too large\n", writer.ToString());
    }

    [Fact]
    public void Report_WritesTheLineBeforeReturning()
    {
        var writer = new StringWriter { NewLine = "\n" };
        IProgress<RunProgress> progress = new RunProgressWriter(writer);

        progress.Report(new RunProgress("file:src/a.cs", UnitKind.File, "src/a.cs", 2, DoneOutcome.Recorded, "recorded 0 finding(s)", 3, 20));
        progress.Report(new RunProgress("slice:M:A.B.C()", UnitKind.Slice, "GET /quotes", 1, DoneOutcome.Recorded, "recorded 1 finding(s)", 4, 20));

        Assert.Equal(
            "3/20 file src/a.cs (attempt 2): recorded 0 finding(s)\n" +
            "4/20 slice GET /quotes (attempt 1): recorded 1 finding(s)\n",
            writer.ToString());
    }
}
