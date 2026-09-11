using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeContentHasher : IContentHasher
{
    public Dictionary<string, string> Hashes { get; } = [];
    public int Calls { get; private set; }

    public Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Hashes.TryGetValue(path, out var hash) ? hash : "hashed-" + path);
    }
}
