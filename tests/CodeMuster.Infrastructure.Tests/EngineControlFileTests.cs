using System.Text;

namespace CodeMuster.Infrastructure.Tests;

public class EngineControlFileTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

    private static void Append(string path, string text)
    {
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes(text);
        file.Write(bytes);
    }

    [Fact]
    public async Task AFileThatDoesNotExistYet_IsWaitedFor_AndItsFirstLineReturned()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "control");
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var control = new EngineControlFile(path, Poll);

        var next = control.NextCommandAsync(guard.Token);
        await Task.Delay(50, guard.Token);
        Assert.False(next.IsCompleted);
        Append(path, "pause\n");

        Assert.Equal("pause", await next);
    }

    [Fact]
    public async Task EachLineIsReturnedOnce_InOrder_WithoutItsLineEnding_AndBlankLinesAreSkipped()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "control");
        Append(path, "pause\r\n\n  \nworkers 2\n{\"command\": \"model\", \"value\": \"café\"}\n");
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var control = new EngineControlFile(path, Poll);

        Assert.Equal("pause", await control.NextCommandAsync(guard.Token));
        Assert.Equal("workers 2", await control.NextCommandAsync(guard.Token));
        Assert.Equal("{\"command\": \"model\", \"value\": \"café\"}", await control.NextCommandAsync(guard.Token));
        var fourth = control.NextCommandAsync(guard.Token);
        Append(path, "resume\n");
        Assert.Equal("resume", await fourth);
    }

    [Fact]
    public async Task APartialLine_WaitsForItsLineFeed()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "control");
        Append(path, "work");
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var control = new EngineControlFile(path, Poll);

        var next = control.NextCommandAsync(guard.Token);
        await Task.Delay(50, guard.Token);
        Assert.False(next.IsCompleted);
        Append(path, "ers 3\n");

        Assert.Equal("workers 3", await next);
    }

    [Fact]
    public async Task AWriterHoldingTheFileOpen_DoesNotBlockReading()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Root, "control");
        using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true, NewLine = "\n" };
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var control = new EngineControlFile(path, Poll);

        writer.WriteLine("stop");

        Assert.Equal("stop", await control.NextCommandAsync(guard.Token));
    }

    [Fact]
    public async Task Waiting_StopsWhenCancelled()
    {
        using var temp = new TempDirectory();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var control = new EngineControlFile(Path.Combine(temp.Root, "control"), Poll);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.NextCommandAsync(cancel.Token));
    }
}
