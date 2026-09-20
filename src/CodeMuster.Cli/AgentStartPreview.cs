using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Cli;

public sealed class AgentStartPreview(
    TextWriter writer,
    IClock clock,
    Func<ConsoleKey?> readKey,
    Func<TimeSpan, CancellationToken, Task> delay,
    TerminalStyle? style = null)
{
    public async Task RunAsync(AgentIdentity identity, int parallelism, bool interactive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var thinking = identity.Effort ?? (identity.Agent == "gemini" ? "not supported" : "provider default");
        if (!interactive)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Running up to {parallelism} {(parallelism == 1 ? "agent" : "agents")}, using {identity.Agent}, Model: {identity.Model ?? "provider default"}, Thinking: {thinking}"));
            return;
        }

        var terminal = style ?? new TerminalStyle(true, false);
        writer.WriteLine();
        writer.WriteLine(terminal.Accent("  </> CODEMUSTER"));
        writer.WriteLine("  " + new string('-', Math.Min(36, Math.Max(1, terminal.Width - 3))));
        writer.WriteLine("  Provider  " + terminal.Accent(identity.Agent.ToUpperInvariant()));
        writer.WriteLine("  Model     " + terminal.Strong(identity.Model ?? "provider default"));
        writer.WriteLine("  Thinking  " + terminal.Thinking(thinking));
        writer.WriteLine("  Workers   " + terminal.Strong(string.Create(CultureInfo.InvariantCulture, $"up to {parallelism}")));
        writer.WriteLine();
        writer.WriteLine("  Enter: start now");
        writer.WriteLine("  Esc/Ctrl+C: cancel");


        var deadline = clock.UtcNow + TimeSpan.FromSeconds(10);
        var displayed = 0;
        try
        {
            while (clock.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = (int)Math.Ceiling((deadline - clock.UtcNow).TotalSeconds);
                if (remaining != displayed)
                {
                    var bar = new string('#', 10 - remaining) + new string('-', remaining);
                    var line = string.Create(CultureInfo.InvariantCulture, $"Starting in {remaining,2}s [{bar}]");
                    writer.Write("\r" + terminal.Paint(terminal.Fit(line), "33"));
                    writer.Flush();
                    displayed = remaining;
                }

                var key = readKey();
                cancellationToken.ThrowIfCancellationRequested();
                if (key == ConsoleKey.Escape) throw new OperationCanceledException(cancellationToken);
                if (key == ConsoleKey.Enter) return;
                await delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            writer.WriteLine();
        }
    }
}
