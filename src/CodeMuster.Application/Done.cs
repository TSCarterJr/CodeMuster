using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Records the model's response for one unit: validates it against the unit, then stores an analysis with its findings, or for a verify unit the verdict on its finding.</summary>
public sealed class Done(ILedger ledger, IClock clock, Config config, AgentIdentity? by = null, IFileSystem? fileSystem = null, string repoRoot = "", ISourceTree? tree = null, IContentHasher? hasher = null)
{
    /// <summary>Stores the response for <paramref name="unitId"/> when it still has <paramref name="fingerprint"/>. An analysis must cite only member paths; it retires the verify units of the findings it replaces before recording, then gives each finding it records a pending verify unit unless verification is off.</summary>
    public async Task<DoneResult> RunAsync(string unitId, string fingerprint, string responseJson, CancellationToken cancellationToken)
    {
        var unit = await ledger.GetUnitAsync(unitId, cancellationToken);
        if (unit is null)
        {
            return new DoneResult(DoneOutcome.Rejected, $"unknown unit {unitId}");
        }

        if (unit.Status == UnitStatus.Retired) return new DoneResult(DoneOutcome.Rejected, unit.Kind == UnitKind.Verify ? Replaced(unit.Id) : "unit is retired; run scan and next again");
        if (unit.Kind == UnitKind.DeadCode) return new DoneResult(DoneOutcome.Rejected, "dead-code assessments are recorded by scan, not by an agent response");
        if (unit.Kind == UnitKind.Ux && !config.UserExperience.Applies(unit.Key))
            return new DoneResult(DoneOutcome.Rejected, "UX review is disabled or no longer applies to this path; run scan");

        if (unit.Fingerprint != fingerprint)
        {
            return new DoneResult(DoneOutcome.Rejected, $"unit {unitId} changed since next; run next again");
        }

        var members = await ledger.GetMembersAsync([unitId], cancellationToken);
        var lensHash = Config.HashOf(config.LensesFor(members.Select(m => (m.Path, Languages.FromPath(m.Path)))));
        var now = Timestamps.Format(clock.UtcNow);
        var current = await ledger.GetCurrentFindingsAsync(cancellationToken);
        var analysis = new Analysis(unitId, fingerprint, lensHash, now, true, null, null, by);
        try
        {
            return unit.Kind switch
            {
                UnitKind.Verify => await RecordVerdictAsync(unit, current, analysis, responseJson, cancellationToken),
                UnitKind.Fix => await RecordFixAsync(unit, current, analysis, responseJson, cancellationToken),
                _ => await RecordFindingsAsync(unit, members, current, analysis, responseJson, cancellationToken),
            };
        }
        catch (JsonException ex)
        {
            await ledger.RecordAnalysisAsync(analysis with { Succeeded = false, Error = ex.Message }, [], cancellationToken);
            return new DoneResult(DoneOutcome.InvalidResponse, $"invalid response: {ex.Message}");
        }
    }

    internal static string Replaced(string unitId) => $"{unitId} tests a finding that a later analysis replaced; run codemuster scan";

    private async Task<DoneResult> RecordFindingsAsync(Unit unit, IReadOnlyList<UnitMember> members, IReadOnlyList<UnitFinding> current, Analysis analysis, string responseJson, CancellationToken cancellationToken)
    {
        var response = AnalysisResponseJson.Parse(responseJson);
        var paths = members.Select(m => m.Path).ToHashSet(StringComparer.Ordinal);
        var findings = response.Findings.Select(f => f with { Path = RepoPath.Normalize(f.Path) }).ToList();
        if (unit.Kind == UnitKind.Ux)
        {
            var evidence = await BrowserEvidenceAsync(responseJson, unit.Fingerprint, unit.Key, cancellationToken);
            analysis = analysis with { EvidenceJson = evidence };
            findings.AddRange(UxEvidence.MeasuredFindings(evidence, unit.Key));
            findings.AddRange(UxEvidence.WorkflowFindings(evidence, unit.Key));
            findings.AddRange(UxEvidence.ExperienceFindings(evidence, unit.Key));
            findings = findings.Distinct().ToList();
            if (findings.Any(f => f.LensId != UxReview.Id || f.Category is not ("ux_readability" or "ux_workflow" or "ux_rendering" or "ux_interaction" or "ux_text" or "ux_recommendation")))
                return new DoneResult(DoneOutcome.Rejected, "UX findings require user_experience lens and a supported ux_readability, ux_workflow, ux_rendering, ux_interaction, ux_text, or ux_recommendation category");
        }
        else if (findings.Any(f => f.Category.StartsWith("ux_", StringComparison.OrdinalIgnoreCase) || f.LensId == UxReview.Id || DeadCodeReview.IsFinding(f)))
            return new DoneResult(DoneOutcome.Rejected, "reserved UX and dead_code findings require their dedicated review units; enable the review setting and scan");
        var outside = findings.FirstOrDefault(f => !paths.Contains(f.Path));
        if (outside is not null)
        {
            return new DoneResult(DoneOutcome.Rejected, $"finding cites {outside.Path}, which is not in unit {unit.Id}");
        }

        await RetireVerifyUnitsAsync(current.Where(f => f.UnitId == unit.Id), cancellationToken);
        await ledger.RecordAnalysisAsync(analysis with { Summary = response.Summary }, findings, cancellationToken);
        if (config.Verify)
        {
            await AddVerifyUnitsAsync(unit, members, cancellationToken);
        }

        return new DoneResult(DoneOutcome.Recorded, string.Create(CultureInfo.InvariantCulture, $"recorded {findings.Count} finding(s)"));
    }

    private async Task<DoneResult> RecordVerdictAsync(Unit unit, IReadOnlyList<UnitFinding> current, Analysis analysis, string responseJson, CancellationToken cancellationToken)
    {
        var finding = current.FirstOrDefault(f => UnitIds.Verify(f.Id) == unit.Id);
        if (finding is null)
        {
            return new DoneResult(DoneOutcome.Rejected, Replaced(unit.Id));
        }

        var source = await ledger.GetUnitAsync(finding.UnitId, cancellationToken);
        if (source?.Kind == UnitKind.DeadCode || DeadCodeReview.IsFinding(finding.Finding))
            return new DoneResult(DoneOutcome.Rejected, "dead-code candidates are report-only static assessments; run scan to refresh them instead of recording a verification outcome");
        var response = VerifyResponseJson.Parse(responseJson);
        if (string.IsNullOrWhiteSpace(response.Reason)) return new DoneResult(DoneOutcome.Rejected, "verification needs an evidence-backed reason");
        if (ReviewEligibility.RequiresBrowser(finding.Finding, source) && response.Verdict != Verdict.Unsure)
        {
            var evidence = await BrowserEvidenceAsync(responseJson, unit.Fingerprint, finding.Finding.Path, cancellationToken);
            if (response.Verdict is Verdict.Refuted or Verdict.Resolved
                && UxEvidence.MeasuredFindings(evidence, finding.Finding.Path)
                    .Concat(UxEvidence.WorkflowFindings(evidence, finding.Finding.Path))
                    .Concat(UxEvidence.ExperienceFindings(evidence, finding.Finding.Path))
                    .Any(MatchesOriginalObservation))
                return new DoneResult(DoneOutcome.Rejected, "browser evidence still records this UX defect; it cannot refute or resolve the finding");
            analysis = analysis with { EvidenceJson = evidence };
        }
        var verdict = response.Verdict.ToString().ToLowerInvariant();
        await ledger.RecordVerificationAsync(analysis with { Summary = $"{verdict}: {response.Reason}" }, finding.Id, response, cancellationToken);
        return new DoneResult(DoneOutcome.Recorded, $"recorded {verdict}");

        bool MatchesOriginalObservation(Finding observed)
        {
            var original = finding.Finding;
            var legacyCategory = source?.Kind == UnitKind.Ux && !original.Category.Trim().StartsWith("ux_", StringComparison.OrdinalIgnoreCase);
            return observed.Path == original.Path
                && (legacyCategory || string.Equals(observed.Category, original.Category.Trim(), StringComparison.OrdinalIgnoreCase))
                && (observed.LineStart <= original.LineEnd && observed.LineEnd >= original.LineStart
                    || string.Equals(observed.Claim, original.Claim, StringComparison.Ordinal));
        }
    }

    private async Task<DoneResult> RecordFixAsync(Unit unit, IReadOnlyList<UnitFinding> current, Analysis analysis, string responseJson, CancellationToken cancellationToken)
    {
        var response = FixResponseJson.Parse(responseJson);
        var sources = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var mine = current
            .Where(f => f.Finding.Path == unit.Key && f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State != FixState.Fixed
                && ReviewEligibility.CanAutoFix(f, sources.GetValueOrDefault(f.UnitId), config))
            .Select(f => f.Id)
            .ToHashSet();
        var cited = response.Addressed.Concat(response.Declined.Select(d => d.Finding)).ToList();
        if (cited.Count == 0)
        {
            return new DoneResult(DoneOutcome.Rejected, $"{unit.Id} addressed or declined nothing; every finding in the pack needs an answer");
        }

        if (cited.FirstOrDefault(id => !mine.Contains(id), -1) is var stray && stray >= 0)
        {
            return new DoneResult(DoneOutcome.Rejected, string.Create(CultureInfo.InvariantCulture, $"finding {stray} is not a confirmed finding in {unit.Key}"));
        }

        if (!mine.SetEquals(cited) || cited.Count != mine.Count || string.IsNullOrWhiteSpace(response.Summary) || response.Declined.Any(d => string.IsNullOrWhiteSpace(d.Reason)))
            return new DoneResult(DoneOutcome.Rejected, "every unresolved confirmed finding needs exactly one outcome and an evidence-backed reason");

        var outcomes = response.Addressed
            .Select(id => (FindingId: id, Outcome: new FixOutcome(FixState.Fixed, response.Summary)))
            .Concat(response.Declined.Select(d => (FindingId: d.Finding, Outcome: new FixOutcome(FixState.Declined, d.Reason))))
            .ToList();
        await ledger.RecordFixAsync(analysis with { Summary = response.Summary }, outcomes, cancellationToken);
        return new DoneResult(DoneOutcome.Recorded, string.Create(CultureInfo.InvariantCulture, $"fixed {response.Addressed.Count} finding(s), declined {response.Declined.Count}"));
    }

    private async Task<string> BrowserEvidenceAsync(string responseJson, string fingerprint, string target, CancellationToken cancellationToken)
    {
        var evidence = UxEvidence.ValidateAndSerialize(responseJson, fingerprint, [target]);
        if (fileSystem is null || tree is null || hasher is null || string.IsNullOrWhiteSpace(repoRoot))
            throw new JsonException("UX completion requires browser artifact verification through the CLI done command");
        using var receipt = JsonDocument.Parse(evidence);
        if (config.UserExperience.BaseUrl is { } baseUrl)
        {
            var configured = new Uri(baseUrl);
            var observed = new Uri(receipt.RootElement.GetProperty("runtime_url").GetString()!);
            if (configured.Scheme != observed.Scheme || configured.Authority != observed.Authority
                || !observed.AbsolutePath.StartsWith(configured.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal) && observed.AbsolutePath != configured.AbsolutePath.TrimEnd('/'))
                throw new JsonException("UX evidence was collected against a different configured application URL");
        }
        await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, target, cancellationToken);
        var lineCount = (await tree.ReadFileAsync(target, cancellationToken)).Split('\n').Length;
        foreach (var page in receipt.RootElement.GetProperty("pages").EnumerateArray())
        {
            var locations = page.GetProperty("readability").GetProperty("contrast_samples").EnumerateArray()
                .Concat(page.GetProperty("workflow").GetProperty("actions").EnumerateArray())
                .Concat(page.GetProperty("experience_checks").EnumerateArray());
            if (locations.Any(location => location.GetProperty("line_end").GetInt32() > lineCount))
                throw new JsonException("UX evidence cites lines outside the current source file");
        }
        await UxArtifacts.ValidateAsync(evidence, repoRoot, fileSystem, cancellationToken);
        return evidence;
    }

    private async Task RetireVerifyUnitsAsync(IEnumerable<UnitFinding> replaced, CancellationToken cancellationToken)
    {
        var retired = new List<Unit>();
        foreach (var finding in replaced)
        {
            if (await ledger.GetUnitAsync(UnitIds.Verify(finding.Id), cancellationToken) is { Status: not UnitStatus.Retired } verify)
            {
                retired.Add(verify with { Status = UnitStatus.Retired });
            }
        }

        if (retired.Count > 0)
        {
            await ledger.UpsertUnitsAsync(retired, await ledger.GetMembersAsync(retired.Select(u => u.Id).ToList(), cancellationToken), cancellationToken);
        }
    }

    private async Task AddVerifyUnitsAsync(Unit unit, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        var planned = (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => f.UnitId == unit.Id)
            .Select(f => PlannedUnit.Verify(f, members, unit.Fidelity))
            .ToList();
        if (planned.Count > 0)
        {
            var created = planned.Select(p => new Unit(p.Id, p.Kind, p.Key, Fingerprints.Compute(p.Members), UnitStatus.Pending, p.Fidelity, null, null, null)).ToList();
            await ledger.UpsertUnitsAsync(created, planned.SelectMany(p => p.Members).ToList(), cancellationToken);
        }
    }
}
