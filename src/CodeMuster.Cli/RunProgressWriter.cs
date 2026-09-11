using System.Globalization;
using CodeMuster.Application;

namespace CodeMuster.Cli;

public sealed class RunProgressWriter(TextWriter writer) : IProgress<RunProgress>
{
    public void Report(RunProgress value) =>
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{value.Completed}/{value.Total} {value.UnitId} (attempt {value.Attempt}): {value.Message}"));
}
