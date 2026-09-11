using CodeMuster.Application;

namespace CodeMuster.Cli.Tests;

public class RunProgressWriterTests
{
    [Fact]
    public void Report_WritesTheLineBeforeReturning()
    {
        var writer = new StringWriter { NewLine = "\n" };
        IProgress<RunProgress> progress = new RunProgressWriter(writer);

        progress.Report(new RunProgress("file:src/a.cs", 2, DoneOutcome.Recorded, "recorded 0 finding(s) for file:src/a.cs", 3, 20));
        progress.Report(new RunProgress("file:src/b.cs", 1, DoneOutcome.Recorded, "recorded 1 finding(s) for file:src/b.cs", 4, 20));

        Assert.Equal(
            "3/20 file:src/a.cs (attempt 2): recorded 0 finding(s) for file:src/a.cs\n" +
            "4/20 file:src/b.cs (attempt 1): recorded 1 finding(s) for file:src/b.cs\n",
            writer.ToString());
    }
}
