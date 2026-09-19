using System.Globalization;
using CodeMuster.Application;

namespace CodeMuster.Cli;

public sealed class RunProgressWriter(TextWriter writer) : IProgress<RunProgress>
{
    public void Report(RunProgress value)
    {
        var attempt = value.Outcome == DoneOutcome.Skipped ? "" : string.Create(CultureInfo.InvariantCulture, $" (attempt {value.Attempt})");
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{value.Completed}/{value.Total} {value.Kind.ToString().ToLowerInvariant()} {value.Key}{attempt}: {value.Message}"));
    }
}
