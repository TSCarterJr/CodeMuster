using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

/// <summary>Reads engine commands (D65) appended to a file, one per line, by polling it: each poll opens the file, reads only the bytes after the last one read, and closes it again, so the same code works on Windows, macOS and Linux and a writer may hold the file open. The file may not exist yet; a line counts once it ends in LF, and blank lines are skipped.</summary>
public sealed class EngineControlFile(string path, TimeSpan? interval = null) : IEngineControl
{
    private readonly TimeSpan poll = interval ?? TimeSpan.FromMilliseconds(250);
    private readonly Queue<string> lines = new();
    private readonly List<byte> partial = [];
    private long offset;

    public async Task<string> NextCommandAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lines.Count == 0) ReadNew();
            if (lines.Count > 0) return lines.Dequeue();
            await Task.Delay(poll, cancellationToken);
        }
    }

    private void ReadNew()
    {
        byte[] added;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length < offset)
            {
                // The controller replaced the file with a shorter one; read the new one from its start.
                offset = 0;
                partial.Clear();
            }

            file.Seek(offset, SeekOrigin.Begin);
            added = new byte[file.Length - offset];
            var read = file.ReadAtLeast(added, added.Length, throwOnEndOfStream: false);
            Array.Resize(ref added, read);
            offset += read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not written yet, or briefly locked by the writer: the next poll tries again.
            return;
        }

        foreach (var value in added)
        {
            if (value != (byte)'\n')
            {
                partial.Add(value);
                continue;
            }

            var line = Encoding.UTF8.GetString(partial.ToArray()).TrimEnd('\r');
            partial.Clear();
            if (line.Trim().Length > 0) lines.Enqueue(line);
        }
    }
}
