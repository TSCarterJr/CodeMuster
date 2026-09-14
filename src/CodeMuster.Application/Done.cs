using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Records the model's response for one unit: validates it against the unit, then stores an analysis with its findings, or for a verify unit the verdict on its finding.</summary>
public sealed class Done(ILedger ledger, IClock clock, Config config, AgentIdentity? by = null)
{
    /// <summary>Stores the response for <paramref name="unitId"/> when it still has <paramref name="fingerprint"/>. An analysis must cite only member paths; it retires the verify units of the findings it replaces before recording, then gives each finding it records a pending verify unit unless verification is off.</summary>
    public async Task<DoneResult> RunAsync(string unitId, string fingerprint, string responseJson, CancellationToken cancellationToken)
    {
        var unit = await ledger.GetUnitAsync(unitId, cancellationToken);
        if (unit is null)
        {
            return new DoneResult(DoneOutcome.Rejected, $"unknown unit {unitId}");
        }

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

        var response = VerifyResponseJson.Parse(responseJson);
        if (string.IsNullOrWhiteSpace(response.Reason)) return new DoneResult(DoneOutcome.Rejected, "verification needs an evidence-backed reason");
        var verdict = response.Verdict.ToString().ToLowerInvariant();
        await ledger.RecordVerificationAsync(analysis with { Summary = $"{verdict}: {response.Reason}" }, finding.Id, response, cancellationToken);
        return new DoneResult(DoneOutcome.Recorded, $"recorded {verdict}");
    }

    private async Task<DoneResult> RecordFixAsync(Unit unit, IReadOnlyList<UnitFinding> current, Analysis analysis, string responseJson, CancellationToken cancellationToken)
    {
        var response = FixResponseJson.Parse(responseJson);
        var mine = current
            .Where(f => f.Finding.Path == unit.Key && f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State != FixState.Fixed)
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
