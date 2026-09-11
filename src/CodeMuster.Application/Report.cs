using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Renders the ledger as markdown: the coverage header, current findings grouped by severity with their verdicts, and the unit inventory (D11, D12). Refuted findings are left out (D27).</summary>
public sealed class Report(ILedger ledger, Config config)
{
    /// <summary>Builds the report; lines are joined with LF and the text ends with one newline.</summary>
    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var status = await new Status(ledger, config).RunAsync(cancellationToken);
        var units = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Status != UnitStatus.Retired)
            .OrderBy(u => u.Key, StringComparer.Ordinal)
            .ToList();
        var current = await ledger.GetCurrentFindingsAsync(cancellationToken);
        var findings = current.Where(f => f.Verification?.Verdict != Verdict.Refuted).ToList();
        var refuted = current.Count - findings.Count;
        var fingerprints = units.ToDictionary(u => u.Id, u => u.Fingerprint);
        var at = status.HeadCommit is null ? "no scan yet" : status.HeadCommit[..Math.Min(7, status.HeadCommit.Length)];

        var lines = new List<string>
        {
            "# CodeMuster report",
            "",
            string.Create(CultureInfo.InvariantCulture, $"analyzed {status.Analyzed}/{status.Total} units at {at}, {status.Stale} stale, {status.LowFidelity} low-fidelity, {status.Excluded} files excluded"),
            "",
            refuted == 0
                ? string.Create(CultureInfo.InvariantCulture, $"## Findings ({findings.Count})")
                : string.Create(CultureInfo.InvariantCulture, $"## Findings ({findings.Count}, {refuted} refuted not shown)"),
        };
        if (findings.Count == 0)
        {
            lines.Add("");
            lines.Add("nothing recorded.");
        }

        foreach (var severity in Enum.GetValues<Severity>())
        {
            var group = findings
                .Where(f => f.Finding.Severity == severity)
                .OrderBy(f => f.Finding.Path, StringComparer.Ordinal)
                .ThenBy(f => f.Finding.LineStart)
                .ToList();
            if (group.Count == 0)
            {
                continue;
            }

            lines.Add("");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"### {Name(severity)} ({group.Count})"));
            lines.Add("");
            foreach (var (_, unitId, fingerprint, finding, verification) in group)
            {
                var verdict = verification is null ? "unverified" : Name(verification.Verdict);
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"- `{FindingLocation.Of(finding)}` [{finding.LensId}, confidence {finding.Confidence:0.00}, {verdict}] {Inline(finding.Claim)}"));
                lines.Add("  " + Inline(finding.Evidence));
                if (verification is not null)
                {
                    lines.Add($"  {verdict}: {Inline(verification.Reason)}");
                }

                if (fingerprints[unitId] != fingerprint)
                {
                    lines.Add("  (stale: unit changed since this analysis)");
                }
            }
        }

        lines.Add("");
        lines.Add("## Units");
        lines.Add("");
        lines.Add("| unit | status | summary |");
        lines.Add("|---|---|---|");
        foreach (var unit in units)
        {
            lines.Add($"| {Cell(unit.Key)} | {Name(unit.Status)} | {Cell(unit.Summary)} |");
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string Inline(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cell(string? text) => Inline(text ?? "").Replace("|", "\\|", StringComparison.Ordinal);

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}
