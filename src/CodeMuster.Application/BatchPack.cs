using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Puts several small units' packs into one agent call and splits the reply back per unit (D80).</summary>
public static class BatchPack
{
    /// <summary>
    /// One markdown document holding each unit's own pack under <c>## Unit k of n: id</c>, its title dropped, its headings one level lower and its response section removed,
    /// followed by one response section asking for <see cref="BatchAnalysisResponseJson.Sample"/>.
    /// </summary>
    public static string Compose(IReadOnlyList<UnitPack> packs)
    {
        var lines = new List<string>
        {
            "# CodeMuster batch",
            "",
            string.Create(CultureInfo.InvariantCulture,
                $"This call reviews {packs.Count} separate units. Review each one on its own, exactly as its section asks and only against its own files, as if it were the only one, then answer all of them in one reply."),
        };
        for (var i = 0; i < packs.Count; i++)
        {
            lines.AddRange(["", string.Create(CultureInfo.InvariantCulture, $"## Unit {i + 1} of {packs.Count}: {packs[i].UnitId}"), ""]);
            lines.AddRange(Nested(packs[i].Markdown));
        }

        lines.AddRange(["", "## Response", "", "Reply with JSON only, in exactly this shape:", "", "```json", BatchAnalysisResponseJson.Sample, "```", "",
            "Answer every unit above exactly once, by its unit id. Each unit's findings must cite paths listed under that unit's Files and set lens_id to the lens they came from. Print the JSON and nothing else; the driver records it for you."]);
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Each answered unit's analysis by unit id; the first answer wins when a unit is answered twice.</summary>
    public static IReadOnlyDictionary<string, AnalysisResponse> Split(string responseJson) =>
        BatchAnalysisResponseJson.Parse(responseJson).Units
            .DistinctBy(unit => unit.Unit, StringComparer.Ordinal)
            .ToDictionary(unit => unit.Unit, unit => new AnalysisResponse(unit.Summary, unit.Findings), StringComparer.Ordinal);

    // Outside code fences, drops the title and the response section and pushes every heading one level down.
    private static IEnumerable<string> Nested(string markdown)
    {
        var fence = 0;
        foreach (var line in markdown.TrimEnd('\n').Split('\n'))
        {
            var ticks = line.TakeWhile(c => c == '`').Count();
            if (fence == 0 && ticks >= 3)
            {
                fence = ticks;
            }
            else if (fence > 0 && ticks >= fence && line.Trim('`').Length == 0)
            {
                fence = 0;
                yield return line;
                continue;
            }

            if (fence == 0 && line == "## Response") yield break;
            if (fence == 0 && line.StartsWith("# ", StringComparison.Ordinal)) continue;
            yield return fence == 0 && line.StartsWith('#') ? "#" + line : line;
        }
    }
}
