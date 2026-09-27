using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Renders the ledger as markdown: the coverage header, current findings grouped by severity with their verdicts, simplification findings in their own section after them (D68), and the inventory of every unit except verify units, whose verdicts show under their findings (D11, D12). Refuted findings are left out unless <paramref name="includeRefuted"/> is set (D27).</summary>
public sealed class Report(ILedger ledger, Config config, bool includeRefuted = false)
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
        var provenance = await ledger.GetProvenanceAsync(cancellationToken);
        var dependencies = current.Where(f => f.Finding.Category == DependencyFindings.Category).ToList();
        var code = current.Where(f => f.Finding.Category != DependencyFindings.Category).ToList();
        var shown = code.Where(f => includeRefuted || f.Verification?.Verdict != Verdict.Refuted).ToList();
        var refuted = code.Count - shown.Count;
        var findings = shown.Where(f => !Config.IsSimplification(f.Finding)).ToList();
        var simplifications = shown.Where(f => Config.IsSimplification(f.Finding)).ToList();
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
        if (status.Skipped > 0) lines.InsertRange(4, [string.Create(CultureInfo.InvariantCulture, $"skipped {status.Skipped} unit(s); these are not analyzed"), ""]);
        if (findings.Count == 0)
        {
            lines.Add("");
            lines.Add("nothing recorded.");
        }

        if (AuditedBy(units, provenance) is { } audited)
        {
            lines.Insert(3, audited);
            lines.Insert(4, "");
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
            group.ForEach(AddFinding);
        }

        if (simplifications.Count > 0)
        {
            lines.Add("");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"## Simplifications ({simplifications.Count})"));
            lines.Add("");
            simplifications.OrderBy(f => f.Finding.Path, StringComparer.Ordinal).ThenBy(f => f.Finding.LineStart).ToList().ForEach(AddFinding);
        }

        void AddFinding(UnitFinding entry)
        {
            var (_, unitId, fingerprint, finding, verification, fix) = entry;
            var verdict = verification is null ? "unverified" : Name(verification.Verdict);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"- `{FindingLocation.Of(finding)}` [{finding.LensId}, confidence {finding.Confidence:0.00}, {verdict}] {Inline(finding.Claim)}"));
            lines.Add("  " + Inline(finding.Evidence));
            if (verification is not null)
            {
                lines.Add($"  {verdict}: {Inline(verification.Reason)}");
            }

            if (fix is not null)
            {
                lines.Add($"  fix: {Name(fix.State)}; {Inline(fix.Reason)}");
            }

            if (fingerprints[unitId] != fingerprint)
            {
                lines.Add("  (stale: unit changed since this analysis)");
            }
        }

        if (dependencies.Count > 0)
        {
            lines.Add("");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"## Vulnerable dependencies ({dependencies.Count})"));
            lines.Add("");
            foreach (var dependency in dependencies
                .OrderBy(f => f.Finding.Severity)
                .ThenBy(f => f.Finding.Path, StringComparer.Ordinal)
                .ThenBy(f => f.Finding.Claim, StringComparer.Ordinal))
            {
                var fixedNote = dependency.Fix is { State: FixState.Fixed } ? " (fixed)" : "";
                lines.Add($"- `{dependency.Finding.Path}` [{Name(dependency.Finding.Severity)}]{fixedNote} {Inline(dependency.Finding.Claim)}");
                lines.Add("  " + Inline(dependency.Finding.Evidence));
            }
        }

        lines.Add("");
        var failures = await ledger.GetFailedAnalysesAsync(cancellationToken);
        if (failures.Count > 0)
        {
            lines.Add("## Failed attempts");
            lines.Add("");
            lines.Add("Historical failures; current status is shown separately. Done can include declined findings and does not prove tests passed.");
            lines.Add("");
            var byId = units.ToDictionary(u => u.Id, StringComparer.Ordinal);
            foreach (var failure in failures)
            {
                var unit = byId[failure.UnitId];
                lines.Add($"- `{Inline(failure.UnitId)}` at {Inline(failure.CreatedAt)}; current status: {Name(unit.Status)}");
                lines.Add("  " + Inline(failure.Error ?? "no reason recorded"));
                if (failure.Fingerprint != unit.Fingerprint)
                {
                    lines.Add("  (stale: unit changed since this attempt)");
                }
            }

            lines.Add("");
        }

        if (status.UxStatus is not null)
        {
            lines.Insert(3, status.UxStatus);
            lines.Insert(4, "");
        }
        var evidence = await ledger.GetLatestEvidenceAsync(cancellationToken);
        foreach (var unit in units.Where(u => u.Kind is UnitKind.Ux or UnitKind.DeadCode))
        {
            lines.Add($"## {(unit.Kind == UnitKind.Ux ? "Browser review" : "Static reachability")}: {Inline(unit.Key)}");
            lines.Add("");
            if (evidence.TryGetValue(unit.Id, out var recorded))
            {
                var currentEvidence = unit.Status == UnitStatus.Done && recorded.Fingerprint == unit.Fingerprint
                    && recorded.LensHash == Config.HashOf(config.LensesFor([(unit.Key, Languages.FromPath(unit.Key))]));
                lines.Add(currentEvidence ? "Recorded evidence for this source fingerprint; findings and verification remain separate."
                    : "Historical evidence: source or review settings changed, or the review is incomplete.");
                lines.Add(unit.Kind == UnitKind.Ux ? "Browser observations cover only the recorded routes, states, themes and viewports. Screenshot bytes and hashes were checked when recorded; runtime/source correspondence is reported by the reviewing agent."
                    : "Static absence of reachability is a candidate, not proof that nothing uses the code; candidates are excluded from automatic deletion.");
                lines.Add("");
                lines.Add("````json");
                using var parsed = JsonDocument.Parse(recorded.EvidenceJson!);
                lines.Add(JsonSerializer.Serialize(parsed.RootElement, DomainJson.Options));
                lines.Add("````");
            }
            else lines.Add(unit.Kind == UnitKind.Ux ? "Incomplete: current browser readability and workflow evidence has not been recorded." : "No static reachability evidence recorded.");
            lines.Add("");
        }

        if (status.Spend is { } spend) lines.AddRange(SpendSection(spend, PriceTable.For(config)));

        lines.Add("## Units");
        lines.Add("");
        lines.Add("| unit | status | summary |");
        lines.Add("|---|---|---|");
        foreach (var unit in units.Where(u => u.Kind != UnitKind.Verify || u.Status == UnitStatus.Skipped))
        {
            lines.Add($"| {Cell(unit.Key)} | {Name(unit.Status)} | {Cell(unit.Summary)} |");
        }

        return string.Join('\n', lines) + "\n";
    }

    private static IEnumerable<string> SpendSection(SpendSummary spend, PriceTable prices)
    {
        yield return "## Spend";
        yield return "";
        yield return string.Create(CultureInfo.InvariantCulture,
            $"{(spend.Unpriced == spend.Calls ? "No priced call:" : Spend.Money(spend.CostUsd) + " API-equivalent across")} {spend.Calls} agent call(s), {spend.Failed} of them failed or rejected; {spend.Unpriced} unpriced.");
        yield return "API-equivalent is what the calls cost at the provider's API prices, whether or not the harness ran on a subscription. Each cost was fixed when its call was recorded, so a later price change does not alter it.";
        yield return "";
        var tokens = spend.Tokens;
        yield return string.Create(CultureInfo.InvariantCulture, $"tokens: {tokens.Input} input, {tokens.Output} output, {tokens.CacheRead} cache read, {tokens.CacheWrite} cache write");
        yield return "";
        yield return "| model | calls | input | output | cache read | cache write | cost |";
        yield return "|---|---|---|---|---|---|---|";
        foreach (var group in spend.ByModel)
        {
            var t = group.Tokens;
            yield return string.Create(CultureInfo.InvariantCulture, $"| {Cell(group.Label)} | {group.Calls} | {t.Input} | {t.Output} | {t.CacheRead} | {t.CacheWrite} | {Spend.Cost(group)} |");
        }

        foreach (var (heading, groups) in new[] { ("unit kind", spend.ByKind), ("run", spend.ByRun) })
        {
            yield return "";
            yield return $"| {heading} | calls | cost |";
            yield return "|---|---|---|";
            foreach (var group in groups) yield return string.Create(CultureInfo.InvariantCulture, $"| {Cell(group.Label)} | {group.Calls} | {Spend.Cost(group)} |");
        }

        yield return "";
        var sources = spend.BySource.Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Key} {entry.Value}"));
        yield return $"prices: bundled table checked {prices.Checked}" + (spend.BySource.Count == 0 ? "" : "; priced calls by source: " + string.Join(", ", sources));
        if (spend.Unpriced > 0)
        {
            var reasons = new List<string>();
            if (spend.WithoutUsage > 0) reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{spend.WithoutUsage} reported no usage"));
            if (spend.UnknownModels.Count > 0) reasons.Add($"{spend.Unpriced - spend.WithoutUsage} used a model that had no price when recorded ({string.Join(", ", spend.UnknownModels)}); add it under \"prices\" in .codemuster/config.json to price later calls");
            yield return string.Create(CultureInfo.InvariantCulture, $"unpriced: {spend.Unpriced} call(s); ") + string.Join("; ", reasons);
        }

        yield return "";
    }

    private static string? AuditedBy(IReadOnlyList<Unit> units, IReadOnlyDictionary<string, AgentIdentity> provenance)
    {
        var counted = units
            .Select(unit => provenance.GetValueOrDefault(unit.Id))
            .OfType<AgentIdentity>()
            .GroupBy(Label, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} ({group.Count()} unit{(group.Count() == 1 ? "" : "s")})"))
            .ToList();
        return counted.Count == 0 ? null : "audited by " + string.Join(", ", counted);
    }

    private static string Label(AgentIdentity identity) =>
        identity.Agent + (identity.Model is null ? "" : " " + identity.Model) + (identity.Effort is null ? "" : "/" + identity.Effort);

    private static string Inline(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cell(string? text) => Inline(text ?? "").Replace("|", "\\|", StringComparison.Ordinal);

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}
