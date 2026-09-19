using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = [];
    public Dictionary<string, byte[]> BinaryFiles { get; } = [];

    public int Writes { get; private set; }
    public bool FailAtomicWrite { get; set; }

    public bool FileExists(string path) => Files.ContainsKey(path) || BinaryFiles.ContainsKey(path);

    public Task<byte[]> ReadBytesAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = BinaryFiles.TryGetValue(path, out var binary) ? binary : System.Text.Encoding.UTF8.GetBytes(Files[path]);
        if (bytes.Length > maxBytes) throw new IOException("artifact exceeds the allowed byte limit");
        return Task.FromResult(bytes);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Files[path]);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        Files[path] = content;
        Writes++;
        return Task.CompletedTask;
    }

    public Task WriteAllTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailAtomicWrite) throw new IOException("simulated atomic replacement failure");
        return WriteAllTextAsync(path, content, cancellationToken);
    }
}
