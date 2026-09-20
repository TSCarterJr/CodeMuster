using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Cli;

public sealed class AgentActivity(TextWriter writer, IClock clock, TerminalStyle style, Func<TimeSpan, CancellationToken, Task> delay)
{
    public async Task<T> RunAsync<T>(string label, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!style.Enabled) return await operation(cancellationToken);
        var started = clock.UtcNow;
        using var animationCancellation = new CancellationTokenSource();
        var animation = AnimateAsync(animationCancellation.Token);
        var outcome = "[ok]";
        var color = "32";
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outcome = "[--] cancelled:";
            color = "33";
            throw;
        }
        catch
        {
            outcome = "[!!] failed:";
            color = "31";
            throw;
        }
        finally
        {
            await animationCancellation.CancelAsync();
            try { await animation; }
            catch (OperationCanceledException) when (animationCancellation.IsCancellationRequested) { }
            WriteFrame(outcome, color);
            writer.WriteLine();
        }

        void WriteFrame(string marker, string code)
        {
            var elapsed = string.Create(CultureInfo.InvariantCulture, $"{(clock.UtcNow - started).TotalSeconds:0}s");
            var prefix = $"{marker} {elapsed} ";
            var line = style.Fit(prefix + label);
            writer.Write("\r" + style.Paint(line.PadRight(Math.Max(1, style.Width - 1)), code));
            writer.Flush();
        }

        async Task AnimateAsync(CancellationToken token)
        {
            var frame = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                WriteFrame("|/-\\"[frame++ % 4].ToString(), "36");
                await delay(TimeSpan.FromMilliseconds(150), token);
            }
        }
    }
}
