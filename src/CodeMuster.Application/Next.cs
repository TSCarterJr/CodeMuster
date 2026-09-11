using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Hands out the next units that need work, each as one markdown pack (D01).</summary>
public sealed class Next(ILedger ledger, ISourceTree tree, Config config, bool interactive = true)
{
    private sealed record Part(UnitMember Member, string Text, bool Outlined);

    /// <summary>Builds a pack for up to <paramref name="batch"/> units; empty when nothing needs work.</summary>
    public async Task<IReadOnlyList<UnitPack>> RunAsync(int batch, CancellationToken cancellationToken)
    {
        var units = await ledger.NextAsync(batch, cancellationToken);
        var members = await ledger.GetMembersAsync(units.Select(u => u.Id).ToList(), cancellationToken);
        var packs = new List<UnitPack>(units.Count);
        foreach (var unit in units)
        {
            var unitMembers = members
                .Where(m => m.UnitId == unit.Id)
                .OrderBy(m => m.Distance)
                .ThenBy(m => m.Path, StringComparer.Ordinal)
                .ThenBy(m => m.Range?.StartLine ?? 0)
                .ToList();
            packs.Add(new UnitPack(unit.Id, unit.Fingerprint, Render(unit, await SelectAsync(unitMembers, cancellationToken))));
        }

        return packs;
    }

    private async Task<IReadOnlyList<Part>> SelectAsync(IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = new List<Part>(members.Count);
        var used = 0L;
        var outlining = false;
        foreach (var member in members)
        {
            if (!contents.TryGetValue(member.Path, out var content))
            {
                content = await tree.ReadFileAsync(member.Path, cancellationToken);
                contents[member.Path] = content;
            }

            if (member.Range is not { } range)
            {
                used += content.Length / 4;
                parts.Add(new Part(member, content, false));
                continue;
            }

            var lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var shown = lines.Skip(range.StartLine - 1).Take(Math.Max(0, Math.Min(range.EndLine, lines.Count) - range.StartLine + 1)).ToList();
            var cost = shown.Sum(line => line.Length + 1) / 4;
            outlining |= member.Distance > 0 && used + cost > config.SliceTokenBudget;
            if (outlining && member.Distance > 0)
            {
                parts.Add(new Part(member, member.Signature ?? "", true));
                continue;
            }

            used += cost;
            parts.Add(new Part(member, Numbered(shown, range), false));
        }

        return parts;
    }

    private static string Numbered(IReadOnlyList<string> lines, LineRange range)
    {
        var width = range.EndLine.ToString(CultureInfo.InvariantCulture).Length;
        return string.Join('\n', lines.Select((line, i) =>
        {
            var number = (range.StartLine + i).ToString(CultureInfo.InvariantCulture).PadLeft(width);
            return line.Length == 0 ? number + " |" : number + " | " + line;
        }));
    }

    private string Render(Unit unit, IReadOnlyList<Part> parts)
    {
        var lenses = config.LensesFor(parts.Select(p => (p.Member.Path, Languages.FromPath(p.Member.Path))));
        var outlined = parts.Count(p => p.Outlined);
        var lines = new List<string>
        {
            "# CodeMuster unit",
            "",
            $"- unit: {unit.Id}",
            $"- kind: {unit.Kind.ToString().ToLowerInvariant()}",
            $"- key: {unit.Key}",
            $"- fingerprint: {unit.Fingerprint}",
            $"- lenses: {string.Join(", ", lenses.Select(l => l.Id))}",
        };
        if (outlined > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"- outlined: {outlined} of {parts.Count} members"));
        }

        lines.Add("");
        lines.Add("## Instructions");
        foreach (var lens in lenses)
        {
            lines.Add("");
            lines.Add($"### {lens.Id}");
            lines.Add("");
            lines.Add(lens.Instructions);
        }

        lines.Add("");
        lines.Add("## Files");

        foreach (var part in parts)
        {
            var member = part.Member;
            var language = Languages.FromPath(member.Path);
            var fence = part.Text.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            var symbol = member.Symbol is null ? "" : $" :: {member.Symbol}";
            lines.Add("");
            lines.Add($"### {member.Path}{symbol} ({language})");
            lines.Add("");
            if (member.Range is { } range)
            {
                var span = range.StartLine == range.EndLine
                    ? string.Create(CultureInfo.InvariantCulture, $"line {range.StartLine}")
                    : string.Create(CultureInfo.InvariantCulture, $"lines {range.StartLine}-{range.EndLine}");
                lines.Add(part.Outlined ? span + ", outlined to its signature to fit the token budget" : span);
                lines.Add("");
            }

            lines.Add(fence + (language == Languages.Unknown ? "text" : language));
            lines.Add(part.Text);
            lines.Add(fence);
        }

        lines.Add("");
        lines.Add("## Response");
        lines.Add("");
        lines.Add("Reply with JSON only, in exactly this shape:");
        lines.Add("");
        lines.Add("```json");
        lines.Add(AnalysisResponseJson.Sample);
        lines.Add("```");
        lines.Add("");
        if (interactive)
        {
            lines.Add("Every finding must cite a path listed under Files and set lens_id to the lens it came from. Then record it with:");
            lines.Add("");
            lines.Add($"    codemuster done {unit.Id} --fingerprint {unit.Fingerprint} --findings <path-to-your-json-file>");
        }
        else
        {
            lines.Add("Every finding must cite a path listed under Files and set lens_id to the lens it came from. Print the JSON and nothing else; the driver records it for you.");
        }

        return string.Join('\n', lines) + "\n";
    }
}
