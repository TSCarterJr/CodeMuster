using System.Security.Cryptography;
using System.Text;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeSourceTree : ISourceTree
{
    public string HeadCommit { get; set; } = "0000000000000000000000000000000000000000";
    public List<SourceFile> Files { get; } = [];
    public Dictionary<string, string> Contents { get; } = [];
    public HashSet<string> Ignored { get; } = new(StringComparer.Ordinal);

    public FakeSourceTree Add(string path, string content, bool linguistGenerated = false) =>
        AddFile(path, content, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(content))), linguistGenerated);

    public FakeSourceTree AddDirty(string path, string content) => AddFile(path, content, null, false);

    private FakeSourceTree AddFile(string path, string content, string? knownHash, bool linguistGenerated)
    {
        Files.RemoveAll(f => f.Path == path);
        Files.Add(new SourceFile(path, content.Length, "2026-09-10T00:00:00.0000000Z", knownHash, null, null, linguistGenerated));
        Contents[path] = content;
        return this;
    }

    public Task<string> HeadCommitAsync(CancellationToken cancellationToken) => Task.FromResult(HeadCommit);

    public Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceFile>>(Files.ToList());

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Contents[path]);

    public Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Ignored.Contains(path));
}
