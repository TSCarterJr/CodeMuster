using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Plans the units of a slice-mode scan from a merged code map (D07, D25).</summary>
public static class SliceBuilder
{
    /// <summary>One slice per entry point whose symbol is in the map and in an included file, in entry point order; a second entry point on the same symbol adds nothing. A slice's members are every symbol a forward breadth-first walk over all edges reaches, each once at its shortest distance with the entry point at 0. The walk passes through symbols in files that are not included but never makes them members.</summary>
    public static IReadOnlyList<PlannedUnit> Build(CompositeMap mapped, IReadOnlyList<FileRecord> included)
    {
        var paths = included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var symbols = mapped.Map.Symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var calls = mapped.Map.Edges.ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);
        return mapped.Map.EntryPoints
            .DistinctBy(e => e.SymbolId, StringComparer.Ordinal)
            .Where(e => symbols.TryGetValue(e.SymbolId, out var entry) && paths.Contains(entry.Path))
            .Select(e => Slice(e, symbols, calls, paths))
            .ToList();
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
}
