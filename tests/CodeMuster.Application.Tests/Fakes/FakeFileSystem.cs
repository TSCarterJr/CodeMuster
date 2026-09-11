using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = [];

    public bool FileExists(string path) => Files.ContainsKey(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Files[path]);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        Files[path] = content;
        return Task.CompletedTask;
    }
}
