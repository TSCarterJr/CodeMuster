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
    public Dictionary<(string Commit, string Path), string> Committed { get; } = [];
    public Dictionary<string, IReadOnlyList<string>> Changed { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CommitStamp> LastCommits { get; } = new(StringComparer.Ordinal);
    public Exception? HistoryError { get; set; }
    public Exception? HeadError { get; set; }
    public int HistoryReads { get; private set; }

    public FakeSourceTree Add(string path, string content, bool linguistGenerated = false) =>
        AddFile(path, content, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(content))), linguistGenerated);

    public FakeSourceTree AddDirty(string path, string content) => AddFile(path, content, null, false);

    private FakeSourceTree AddFile(string path, string content, string? knownHash, bool linguistGenerated)
    {
        Files.RemoveAll(f => f.Path == path);
        Files.Add(new SourceFile(path, content.Length, "2026-09-10T00:00:00.0000000Z", knownHash, linguistGenerated));
        Contents[path] = content;
        return this;
    }

    public Task<string> HeadCommitAsync(CancellationToken cancellationToken) =>
        HeadError is null ? Task.FromResult(HeadCommit) : Task.FromException<string>(HeadError);

    public Task<IReadOnlyList<SourceFile>> ListFilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceFile>>(Files.ToList());

    public Task<IReadOnlyDictionary<string, CommitStamp>> LastCommitsAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        HistoryReads++;
        return HistoryError is not null
            ? Task.FromException<IReadOnlyDictionary<string, CommitStamp>>(HistoryError)
            : Task.FromResult<IReadOnlyDictionary<string, CommitStamp>>(LastCommits.Where(entry => paths.Contains(entry.Key)).ToDictionary(StringComparer.Ordinal));
    }

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Contents[path]);

    public Task<string?> ReadFileAtCommitAsync(string commit, string path, CancellationToken cancellationToken) =>
        Task.FromResult(Committed.GetValueOrDefault((commit, path)));

    public Task<IReadOnlyList<string>> ChangedSinceAsync(string since, CancellationToken cancellationToken) =>
        Changed.TryGetValue(since, out var paths) ? Task.FromResult(paths) : Task.FromException<IReadOnlyList<string>>(new InvalidOperationException($"git diff: unknown revision {since}"));

    public Task<bool> IsIgnoredAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Ignored.Contains(path));
}
