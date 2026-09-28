using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Runs every mapper that has code to map and merges what they return into one map (D08, D25).</summary>
public static class CompositeMapper
{
    private const int MaxUnresolvedNames = 20;

    /// <summary>Runs, in order, each mapper with at least one of the <paramref name="included"/> files in one of its languages, handing it every included path. Each of its languages that has an included file is reported mapped, or failed when it throws, except a language the mapper returns as skipped, which is neither and whose note goes to <paramref name="progress"/>; a mapper that throws also adds the diagnostic <c>&lt;language&gt; mapper failed: &lt;message&gt;</c> under its main language; cancellation propagates. Each mapper's progress reaches <paramref name="progress"/> as <c>&lt;language&gt;: &lt;step&gt;</c>.</summary>
    public static async Task<CompositeMap> MapAsync(IReadOnlyList<ICodeMapper> mappers, string repoRoot, IReadOnlyList<FileRecord> included, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var paths = included.Select(f => f.Path).ToList();
        var maps = new List<CodeMap>();
        var mapped = new List<string>();
        var failed = new List<string>();
        var diagnostics = new List<string>();
        foreach (var mapper in mappers)
        {
            var present = mapper.Languages.Where(language => included.Any(f => f.Language == language)).ToList();
            if (present.Count == 0)
            {
                continue;
            }

            try
            {
                var map = await mapper.MapAsync(repoRoot, paths, PrefixedProgress.For(progress, mapper.Language), cancellationToken);
                maps.Add(map);
                mapped.AddRange(present.Where(language => !map.SkippedLanguages.Any(skipped => skipped.Language == language)));
                map.SkippedLanguages.ToList().ForEach(skipped => progress?.Report(skipped.Note));
                diagnostics.AddRange(map.Diagnostics);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed.AddRange(present);
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
            diagnostics)
        {
            HttpCalls = maps.SelectMany(m => m.HttpCalls).ToList(),
            UiElements = maps.SelectMany(m => m.UiElements).ToList(),
            Declarations = maps.SelectMany(m => m.Declarations).ToList(),
            References = maps.SelectMany(m => m.References).ToList(),
        };
        return new CompositeMap(merged, failed, mapped);
    }
}
