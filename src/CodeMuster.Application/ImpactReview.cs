using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>What a changed symbol reaches in a code map, within the caps an impact unit uses (D67).</summary>
/// <param name="Callers">Callers within <see cref="ImpactReview.CallerDepth"/> calls up and <see cref="ImpactReview.CallerNodes"/> symbols, each with how many calls up it is, nearest first.</param>
/// <param name="CallersCut">Callers the caps left out.</param>
/// <param name="Upward">Every caller up to <see cref="CodeMapQuery.NodeCap"/> symbols, however far up, nearest first.</param>
/// <param name="Callees">Symbols the changed symbol calls directly, up to <see cref="ImpactReview.CalleeNodes"/>.</param>
/// <param name="CalleesCut">Direct callees the cap left out.</param>
/// <param name="EntryPoints">Entry points on the changed symbol or any caller in <paramref name="Upward"/>, with how many calls up each is, nearest first.</param>
public sealed record ImpactReach(
    IReadOnlyList<(Symbol Symbol, int Depth)> Callers,
    int CallersCut,
    IReadOnlyList<(Symbol Symbol, int Depth)> Upward,
    IReadOnlyList<Symbol> Callees,
    int CalleesCut,
    IReadOnlyList<(EntryPoint Entry, int Depth)> EntryPoints);

/// <summary>
/// Middle-out impact review (D67): a symbol whose body hash or signature changed since the stored map gets one <see cref="UnitKind.Impact"/> unit
/// holding its previous version, its current version, its callers walked up every edge kind (http included) and its direct callees.
/// </summary>
public static class ImpactReview
{
    /// <summary>How many calls up the callers that become members may be.</summary>
    public const int CallerDepth = 4;

    /// <summary>The most callers that become members.</summary>
    public const int CallerNodes = 40;

    /// <summary>The most direct callees that become members.</summary>
    public const int CalleeNodes = 40;

    /// <summary>The lens id impact findings carry.</summary>
    public const string LensId = "impact";

    /// <summary>What an impact pack asks.</summary>
    public const string Instructions =
        "The symbol below changed since the previous scan. Its previous and current text, the callers that reach it up to their entry points, and the symbols it calls are shown. "
        + "Answer one question: does anything upstream or downstream now break or misuse the change? Check changed return values or meaning, new exceptions or nulls, "
        + "changed parameters, routes the UI still calls, and callees now used incorrectly. Report only breakage the code shown evidences, cite exact lines in the caller or the changed symbol, "
        + "and set lens_id to \"impact\". Report nothing when every caller and callee still fits the change.";

    /// <summary>
    /// One impact unit per symbol in an included file whose id is in both maps with a different body hash or signature; new and deleted symbols are not impact targets.
    /// Members: the previous version at distance -1 (its symbol written <c>id@commit</c>, so the pack can read it from git), the current version at 0, the callers within the caps at their distance,
    /// and the direct callees at 1; callers and callees in files that are not included are walked through but never become members.
    /// </summary>
    public static IReadOnlyList<PlannedUnit> Plan(StoredCodeMap? previous, CodeMap current, IReadOnlyList<FileRecord> included)
    {
        if (previous is null)
        {
            return [];
        }

        var paths = included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var before = previous.Map.Symbols.DistinctBy(s => s.Id, StringComparer.Ordinal).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var units = new List<PlannedUnit>();
        foreach (var symbol in current.Symbols.DistinctBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!paths.Contains(symbol.Path) || !before.TryGetValue(symbol.Id, out var old) || old.BodyHash == symbol.BodyHash && old.Signature == symbol.Signature)
            {
                continue;
            }

            var id = UnitIds.Impact(symbol.Id);
            var reach = Walk(current, symbol.Id);
            var members = new List<UnitMember>
            {
                new(id, old.Path, PreviousSymbol(old.Id, previous.HeadCommit), SliceBuilder.MemberHash(old), -1, old.Range, old.Signature),
                Member(id, symbol, 0),
            };
            var added = new HashSet<string>(StringComparer.Ordinal) { symbol.Id };
            members.AddRange(reach.Callers.Where(c => paths.Contains(c.Symbol.Path) && added.Add(c.Symbol.Id)).Select(c => Member(id, c.Symbol, c.Depth)));
            members.AddRange(reach.Callees.Where(c => paths.Contains(c.Path) && added.Add(c.Id)).Select(c => Member(id, c, 1)));
            units.Add(new PlannedUnit(id, UnitKind.Impact, CodeMapQuery.ShortName(symbol), Fidelity.Full, members));
        }

        return units;
    }

    /// <summary>The member symbol that names a changed symbol's previous version at the commit the stored map was taken.</summary>
    public static string PreviousSymbol(string symbolId, string commit) => symbolId + "@" + commit;

    /// <summary>Splits <see cref="PreviousSymbol"/> at its last '@', because C# ids use '@' for ref parameters.</summary>
    public static (string SymbolId, string Commit) SplitPrevious(string previousSymbol)
    {
        var at = previousSymbol.LastIndexOf('@');
        return at < 0 ? (previousSymbol, "") : (previousSymbol[..at], previousSymbol[(at + 1)..]);
    }

    /// <summary>Walks <paramref name="map"/> up and down from <paramref name="symbolId"/> breadth-first, never revisiting a symbol.</summary>
    public static ImpactReach Walk(CodeMap map, string symbolId)
    {
        var symbols = map.Symbols.DistinctBy(s => s.Id, StringComparer.Ordinal).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var edges = map.Edges.Distinct().ToList();
        var incoming = edges.ToLookup(e => e.To, e => e.From, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [symbolId] = 0 };
        var upward = new List<(Symbol Symbol, int Depth)>();
        var queue = new Queue<string>([symbolId]);
        while (queue.TryDequeue(out var id) && upward.Count < CodeMapQuery.NodeCap)
        {
            foreach (var caller in incoming[id].Where(symbols.ContainsKey))
            {
                if (upward.Count < CodeMapQuery.NodeCap && depths.TryAdd(caller, depths[id] + 1))
                {
                    upward.Add((symbols[caller], depths[caller]));
                    queue.Enqueue(caller);
                }
            }
        }

        var callers = upward.Where(c => c.Depth <= CallerDepth).Take(CallerNodes).ToList();
        var direct = edges.Where(e => e.From == symbolId && e.To != symbolId && symbols.ContainsKey(e.To)).Select(e => e.To).Distinct(StringComparer.Ordinal).ToList();
        var entries = map.EntryPoints.Distinct()
            .Where(e => depths.ContainsKey(e.SymbolId))
            .Select(e => (Entry: e, Depth: depths[e.SymbolId]))
            .OrderBy(e => e.Depth)
            .ThenBy(e => e.Entry.Display, StringComparer.Ordinal)
            .ToList();
        return new ImpactReach(callers, upward.Count - callers.Count, upward, direct.Take(CalleeNodes).Select(id => symbols[id]).ToList(), Math.Max(0, direct.Count - CalleeNodes), entries);
    }

    /// <summary>The pack sections between the instructions and the files: the changed symbol with its previous text, the entry points and pages reached, and what the caps cut.</summary>
    public static IReadOnlyList<string> Sections(Unit unit, UnitMember? previous, UnitMember? current, string? previousText, ImpactReach reach)
    {
        var lines = new List<string> { "", "## Changed symbol", "" };
        var location = current?.Range is { } range ? $"{current.Path}:{Span(range)}" : current?.Path ?? "";
        var (_, commit) = previous?.Symbol is { } symbol ? SplitPrevious(symbol) : ("", "");
        var shortCommit = commit.Length > 7 ? commit[..7] : commit;
        lines.Add(previous?.Range is { } before
            ? $"{unit.Key} at {location}, previously {previous.Path}:{Span(before)}."
            : $"{unit.Key} at {location}.");
        lines.Add("");
        lines.Add($"### Previous text (committed at {shortCommit})");
        lines.Add("");
        if (previous?.Range is { } old && previousText is not null)
        {
            var shown = previousText.Split('\n').Select(line => line.TrimEnd('\r')).Skip(old.StartLine - 1).Take(Math.Max(0, old.EndLine - old.StartLine + 1)).ToList();
            var language = Languages.FromPath(previous.Path);
            lines.Add("This is the text committed at that commit; if that scan mapped uncommitted edits, what it saw may differ.");
            lines.Add("");
            lines.Add("```" + (language == Languages.Unknown ? "text" : language));
            lines.Add(Numbered(shown, old.StartLine));
            lines.Add("```");
        }
        else
        {
            lines.Add($"not available: {previous?.Path} is not in commit {shortCommit}");
        }

        lines.AddRange(["", "## Entry points and pages reached", ""]);
        if (reach.EntryPoints.Count == 0)
        {
            lines.Add("- none: no entry point in the map reaches this symbol");
        }

        foreach (var (entry, depth) in reach.EntryPoints)
        {
            var distance = depth == 0 ? "the changed symbol itself" : string.Create(CultureInfo.InvariantCulture, $"{depth} call{(depth == 1 ? "" : "s")} up");
            var cut = depth > CallerDepth ? ", beyond the depth cap"
                : depth > 0 && !reach.Callers.Any(c => c.Symbol.Id == entry.SymbolId) ? ", beyond the node cap"
                : "";
            lines.Add($"- {entry.Display} ({entry.Kind}), {distance}{cut}");
        }

        lines.AddRange(["", "## Caps", ""]);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"- callers up to {CallerDepth} calls up and {CallerNodes} symbols: {reach.Callers.Count} shown, {reach.CallersCut} more not shown"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"- callees one call down, up to {CalleeNodes} symbols: {reach.Callees.Count} shown as signatures, {reach.CalleesCut} more not shown"));
        return lines;
    }

    private static string Span(LineRange range) => range.StartLine == range.EndLine
        ? range.StartLine.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{range.StartLine}-{range.EndLine}");

    private static string Numbered(IReadOnlyList<string> lines, int start)
    {
        var width = (start + Math.Max(0, lines.Count - 1)).ToString(CultureInfo.InvariantCulture).Length;
        return string.Join('\n', lines.Select((line, i) =>
        {
            var number = (start + i).ToString(CultureInfo.InvariantCulture).PadLeft(width);
            return line.Length == 0 ? number + " |" : number + " | " + line;
        }));
    }

    private static UnitMember Member(string unitId, Symbol symbol, int distance) =>
        new(unitId, symbol.Path, symbol.Id, SliceBuilder.MemberHash(symbol), distance, symbol.Range, symbol.Signature);
}
