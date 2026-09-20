using System.Diagnostics;
using System.Globalization;

namespace CodeMuster.Cli;

public sealed class ProgressWriter(TextWriter writer, TerminalStyle? style = null) : IProgress<string>
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public void Report(string value)
    {
        var elapsed = string.Create(CultureInfo.InvariantCulture, $"[{stopwatch.Elapsed.TotalSeconds,3:0} s]");
        writer.WriteLine($"{(style is null ? elapsed : style.Accent(elapsed))} {value}");
    }
}
