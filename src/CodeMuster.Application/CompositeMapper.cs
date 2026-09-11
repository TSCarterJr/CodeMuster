using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Runs every mapper that has code to map and merges what they return into one map (D08, D25).</summary>
public static class CompositeMapper
{
    private const int MaxUnresolvedNames = 20;

    /// <summary>Runs, in order, each mapper whose language has at least one of the <paramref name="included"/> files, handing it every included path. A mapper that throws adds the diagnostic <c>&lt;language&gt; mapper failed: &lt;message&gt;</c> and marks its language failed; cancellation propagates.</summary>
    public static async Task<CompositeMap> MapAsync(IReadOnlyList<ICodeMapper> mappers, string repoRoot, IReadOnlyList<FileRecord> included, CancellationToken cancellationToken)
    {
        var paths = included.Select(f => f.Path).ToList();
        var maps = new List<CodeMap>();
        var failed = new List<string>();
        var diagnostics = new List<string>();
        foreach (var mapper in mappers.Where(m => included.Any(f => f.Language == m.Language)))
        {
            try
            {
                var map = await mapper.MapAsync(repoRoot, paths, cancellationToken);
                maps.Add(map);
                diagnostics.AddRange(map.Diagnostics);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed.Add(mapper.Language);
                diagnostics.Add($"{mapper.Language} mapper failed: {ex.Message}");
            }
        }

        var names = maps
            .SelectMany(m => m.Resolution.TopUnresolvedNames.Select((name, rank) => (name, rank)))
            .OrderBy(n => n.rank)
            .Select(n => n.name)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxUnresolvedNames)
            .ToList();
        var merged = new CodeMap(
            maps.SelectMany(m => m.Symbols).ToList(),
            maps.SelectMany(m => m.Edges).ToList(),
            maps.SelectMany(m => m.EntryPoints).ToList(),
            new ResolutionStats(maps.Sum(m => m.Resolution.Resolved), maps.Sum(m => m.Resolution.Unresolved), names),
            diagnostics);
        return new CompositeMap(merged, failed);
    }
}
