using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeCodeMapper(string language, CodeMap map) : ICodeMapper
{
    public string Language => language;
    public CodeMap Map { get; set; } = map;
    public Exception? Throws { get; set; }
    public List<(string RepoRoot, IReadOnlyList<string> Paths)> Calls { get; } = [];
    public Action? OnMap { get; set; }
    public List<string> Reports { get; } = [];

    public Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Calls.Add((repoRoot, paths));
        OnMap?.Invoke();
        Reports.ForEach(message => progress?.Report(message));
        return Throws is null ? Task.FromResult(Map) : Task.FromException<CodeMap>(Throws);
    }
}
