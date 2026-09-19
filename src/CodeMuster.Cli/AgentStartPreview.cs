using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Cli;

public sealed class AgentStartPreview(
    TextWriter writer,
    IClock clock,
    Func<ConsoleKey?> readKey,
    Func<TimeSpan, CancellationToken, Task> delay)
{
    public async Task RunAsync(AgentIdentity identity, int parallelism, bool interactive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var thinking = identity.Effort ?? (identity.Agent == "gemini" ? "not supported" : "provider default");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Running up to {parallelism} {(parallelism == 1 ? "agent" : "agents")}, using {identity.Agent}, Model: {identity.Model ?? "provider default"}, Thinking: {thinking}"));
        if (!interactive) return;

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
                    writer.Write(string.Create(CultureInfo.InvariantCulture,
                        $"\rStarting in {remaining,2}s | Enter: start now | Esc/Ctrl+C: cancel"));
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
