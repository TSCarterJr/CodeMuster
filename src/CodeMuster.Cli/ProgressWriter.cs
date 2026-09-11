using System.Diagnostics;
using System.Globalization;

namespace CodeMuster.Cli;

public sealed class ProgressWriter(TextWriter writer) : IProgress<string>
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public void Report(string value) =>
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{stopwatch.Elapsed.TotalSeconds,3:0} s] {value}"));
}
