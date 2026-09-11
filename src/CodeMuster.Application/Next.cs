using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Hands out the next units that need work, each as one markdown pack (D01).</summary>
public sealed class Next(ILedger ledger, ISourceTree tree, Config config)
{
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
                .ToList();
            packs.Add(new UnitPack(unit.Id, unit.Fingerprint, await RenderAsync(unit, unitMembers, cancellationToken)));
        }

        return packs;
    }

    private async Task<string> RenderAsync(Unit unit, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        var lenses = config.LensesFor(members.Select(m => (m.Path, Languages.FromPath(m.Path))));
        var lines = new List<string>
        {
            "# CodeMuster unit",
            "",
            $"- unit: {unit.Id}",
            $"- kind: {unit.Kind.ToString().ToLowerInvariant()}",
            $"- fingerprint: {unit.Fingerprint}",
            $"- lenses: {string.Join(", ", lenses.Select(l => l.Id))}",
            "",
            "## Instructions",
        };

        foreach (var lens in lenses)
        {
            lines.Add("");
            lines.Add($"### {lens.Id}");
            lines.Add("");
            lines.Add(lens.Instructions);
        }

        lines.Add("");
        lines.Add("## Files");

        foreach (var member in members)
        {
            var content = await tree.ReadFileAsync(member.Path, cancellationToken);
            var language = Languages.FromPath(member.Path);
            var fence = content.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            var symbol = member.Symbol is null ? "" : $" :: {member.Symbol}";
            lines.Add("");
            lines.Add($"### {member.Path}{symbol} ({language})");
            lines.Add("");
            lines.Add(fence + (language == Languages.Unknown ? "text" : language));
            lines.Add(content);
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
        lines.Add("Every finding must cite a path listed under Files and set lens_id to the lens it came from. Then record it with:");
        lines.Add("");
        lines.Add($"    codemuster done {unit.Id} --fingerprint {unit.Fingerprint} --findings <path-to-your-json-file>");

        return string.Join('\n', lines) + "\n";
    }
}
