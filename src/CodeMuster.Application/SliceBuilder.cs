using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Plans the units of a slice-mode scan from a merged code map (D07, D25).</summary>
public static class SliceBuilder
{
    /// <summary>
    /// Plans slices first, then for each included file in order its orphan or file unit, so every included file and every mapped symbol in one belongs to at least one unit (D25).
    /// There is one slice per entry point whose symbol is in the map and in an included file; a second entry point on the same symbol adds nothing. A slice's members are every symbol a forward breadth-first walk over all edges reaches, each once at its shortest distance with the entry point at 0; the walk passes through symbols in files that are not included but never makes them members.
    /// A file with symbols that no slice reaches gets one orphan unit holding those symbols, or the whole file when none of its symbols is reached. A file whose language's mapper failed, or with no symbols, gets a whole-file unit, at low fidelity only when its mapper failed.
    /// </summary>
    public static IReadOnlyList<PlannedUnit> Build(CompositeMap mapped, IReadOnlyList<FileRecord> included)
    {
        var paths = included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var symbols = mapped.Map.Symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var calls = mapped.Map.Edges.ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);
        var slices = mapped.Map.EntryPoints
            .DistinctBy(e => e.SymbolId, StringComparer.Ordinal)
            .Where(e => symbols.TryGetValue(e.SymbolId, out var entry) && paths.Contains(entry.Path))
            .Select(e => Slice(e, symbols, calls, paths))
            .ToList();

        var reached = slices.SelectMany(s => s.Members).Select(m => m.Symbol).ToHashSet(StringComparer.Ordinal);
        var byPath = mapped.Map.Symbols.ToLookup(s => s.Path, StringComparer.Ordinal);
        var rest = included
            .Select(file => Unsliced(file, byPath[file.Path].ToList(), reached, mapped.FailedLanguages.Contains(file.Language)))
            .OfType<PlannedUnit>();
        return [.. slices, .. rest];
    }

    private static PlannedUnit Slice(EntryPoint entry, Dictionary<string, Symbol> symbols, ILookup<string, string> calls, HashSet<string> paths)
    {
        var id = UnitIds.Slice(entry.SymbolId);
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [entry.SymbolId] = 0 };
        var queue = new Queue<string>([entry.SymbolId]);
        var members = new List<UnitMember>();
        while (queue.TryDequeue(out var current))
        {
            var symbol = symbols[current];
            if (paths.Contains(symbol.Path))
            {
                members.Add(new UnitMember(id, symbol.Path, symbol.Id, symbol.BodyHash, distances[current], symbol.Range, symbol.Signature));
            }

            foreach (var next in calls[current].Where(symbols.ContainsKey))
            {
                if (distances.TryAdd(next, distances[current] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return new PlannedUnit(id, UnitKind.Slice, entry.Display, Fidelity.Full, members);
    }

    private static PlannedUnit? Unsliced(FileRecord file, List<Symbol> symbols, HashSet<string?> reached, bool mapperFailed)
    {
        if (mapperFailed || symbols.Count == 0)
        {
            var fileId = UnitIds.File(file.Path);
            return new PlannedUnit(fileId, UnitKind.File, file.Path, mapperFailed ? Fidelity.Low : Fidelity.Full, [WholeFile(fileId, file)]);
        }

        var unreached = symbols.Where(s => !reached.Contains(s.Id)).ToList();
        if (unreached.Count == 0)
        {
            return null;
        }

        var id = UnitIds.Orphan(file.Path);
        IReadOnlyList<UnitMember> members = unreached.Count == symbols.Count
            ? [WholeFile(id, file)]
            : unreached.Select(s => new UnitMember(id, s.Path, s.Id, s.BodyHash, 0, s.Range, s.Signature)).ToList();
        return new PlannedUnit(id, UnitKind.Orphan, file.Path, Fidelity.Full, members);
    }

    private static UnitMember WholeFile(string unitId, FileRecord file) => new(unitId, file.Path, null, file.ContentHash, 0);
}
