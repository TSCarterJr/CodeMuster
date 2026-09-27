using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>One group of copies: symbols in one language with the same normalized body hash (D68).</summary>
/// <param name="Id">The duplicate unit's id.</param>
/// <param name="Copies">Every copy, ordered by path, then line, then id.</param>
public sealed record DuplicateGroup(string Id, IReadOnlyList<Symbol> Copies);

/// <summary>
/// Duplicate review (D68): symbols whose normalized body hash matches (identifiers and literals replaced) are copies of one another.
/// Scan plans one <see cref="UnitKind.Duplicate"/> unit per group of at least two copies, in one language, each spanning at least <see cref="MinLines"/> lines and in an included file,
/// and the pack asks whether and where to consolidate them.
/// </summary>
public static class DuplicateReview
{
    /// <summary>The fewest lines a copy spans.</summary>
    public const int MinLines = 6;

    /// <summary>The most copies one unit holds; the pack states how many more there are.</summary>
    public const int CopyCap = 12;

    /// <summary>The lens id duplicate findings carry.</summary>
    public const string LensId = "duplicate";

    /// <summary>What a duplicate pack asks.</summary>
    public const string Instructions =
        "The symbols below have the same normalized body: the same code once identifiers and literals are set aside, each at least 6 lines. "
        + "Decide whether they should be consolidated, where the shared version should live, and what it would look like. "
        + "When they should, report one finding per copy with category \"simplification\" and severity \"low\", citing that copy's lines, "
        + "and say in the claim where the shared version belongs and in the evidence what it would look like. "
        + "Report nothing when the duplication is appropriate: tests, generated DTOs, deliberately separate domains, or code that only looks alike. "
        + "Set lens_id to \"duplicate\".";

    /// <summary>Every group of at least two copies among the symbols in <paramref name="included"/> paths, ordered by id. Symbols without a normalized hash never group, and neither do two languages.</summary>
    public static IReadOnlyList<DuplicateGroup> Groups(CodeMap map, IReadOnlySet<string> included)
    {
        var groups = map.Symbols
            .DistinctBy(s => s.Id, StringComparer.Ordinal)
            .Where(s => s.NormalizedHash is not null && included.Contains(s.Path) && s.Range.EndLine - s.Range.StartLine + 1 >= MinLines)
            .GroupBy(s => (Hash: s.NormalizedHash!, Language: Languages.FromPath(s.Path)))
            .Where(g => g.Count() >= 2)
            .ToList();
        var languages = groups.GroupBy(g => g.Key.Hash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return groups
            .Select(g => new DuplicateGroup(
                UnitIds.Duplicate(g.Key.Hash, languages[g.Key.Hash] > 1 ? g.Key.Language : null),
                g.OrderBy(s => s.Path, StringComparer.Ordinal).ThenBy(s => s.Range.StartLine).ThenBy(s => s.Id, StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One unit per group: its first <see cref="CopyCap"/> copies as symbol members, keyed by the first copy's name and how many others there are.</summary>
    public static IReadOnlyList<PlannedUnit> Plan(CodeMap map, IReadOnlyList<FileRecord> included) =>
        Groups(map, included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal))
            .Select(g => new PlannedUnit(g.Id, UnitKind.Duplicate, Key(g), Fidelity.Full,
                g.Copies.Take(CopyCap).Select(s => new UnitMember(g.Id, s.Path, s.Id, SliceBuilder.MemberHash(s), 0, s.Range, s.Signature)).ToList()))
            .ToList();

    /// <summary>The pack section listing the copies, with the cap when <paramref name="total"/> exceeds the members shown.</summary>
    public static IReadOnlyList<string> Sections(IReadOnlyList<UnitMember> members, int total, IReadOnlyDictionary<string, Symbol> symbols)
    {
        var lines = new List<string>
        {
            "",
            "## Copies",
            "",
            string.Create(CultureInfo.InvariantCulture, $"{members.Count} copies with the same normalized body, each at least {MinLines} lines:"),
            "",
        };
        lines.AddRange(members.Select(m => string.Create(CultureInfo.InvariantCulture,
            $"- {m.Path}:{m.Range?.StartLine}-{m.Range?.EndLine} {(m.Symbol is { } id && symbols.TryGetValue(id, out var symbol) ? CodeMapQuery.ShortName(symbol) : m.Symbol)}")));
        if (total > members.Count)
        {
            lines.AddRange(["", string.Create(CultureInfo.InvariantCulture, $"{total} copies in all; capped at the first {CopyCap} by path, which are shown and are this unit's members.")]);
        }

        return lines;
    }

    private static string Key(DuplicateGroup group)
    {
        var others = group.Copies.Count - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{CodeMapQuery.ShortName(group.Copies[0])} and {others} other cop{(others == 1 ? "y" : "ies")}");
    }
}
