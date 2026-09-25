using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeContentHasher : IContentHasher
{
    public Dictionary<string, string> Hashes { get; } = [];
    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<string, string>> HashFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(paths.Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => Hashes.TryGetValue(path, out var hash) ? hash : "hashed-" + path, StringComparer.Ordinal));
    }
}
