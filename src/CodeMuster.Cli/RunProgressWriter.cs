using System.Globalization;
using CodeMuster.Application;

namespace CodeMuster.Cli;

public sealed class RunProgressWriter(TextWriter writer, TerminalStyle? style = null) : IProgress<RunProgress>
{
    public void Report(RunProgress value)
    {
        var attempt = value.Outcome == DoneOutcome.Skipped ? "" : string.Create(CultureInfo.InvariantCulture, $" (attempt {value.Attempt})");
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{value.Completed}/{value.Total} {value.Kind.ToString().ToLowerInvariant()} {value.Key}{attempt}: {value.Message}");
        if (style?.Enabled == true)
        {
            var filled = value.Total <= 0 ? 0 : (int)Math.Clamp(10L * value.Completed / value.Total, 0, 10);
            var bar = "[" + new string('#', filled) + new string('-', 10 - filled) + "] ";
            var color = value.Outcome switch
            {
                DoneOutcome.Recorded => "32",
                DoneOutcome.Skipped => "33",
                _ => "31",
            };
            line = style.Paint(bar + line, color);
        }
        writer.WriteLine(line);
    }
}
