using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Groups the current findings of one reporting unit into verify units (D78).</summary>
public static class VerifyBatches
{
    /// <summary>
    /// Keeps every live verify unit already answered (done, stale or skipped) whose findings are all still current, then groups the findings no answer covers, in id order, into units of at most <paramref name="size"/>.
    /// A finding a done batch left unanswered is regrouped. A finding that needs browser evidence is always verified alone.
    /// </summary>
    public static IReadOnlyList<PlannedUnit> Plan(Unit? source, IReadOnlyList<UnitFinding> findings, IReadOnlyList<UnitMember> members, Fidelity fidelity, int size, IEnumerable<Unit> live)
    {
        var byId = findings.ToDictionary(f => f.Id);
        var planned = new List<PlannedUnit>();
        var covered = new HashSet<long>();
        foreach (var unit in live.Where(u => u.Kind == UnitKind.Verify && u.Status is UnitStatus.Done or UnitStatus.Stale or UnitStatus.Skipped).OrderBy(u => u.Id, StringComparer.Ordinal))
        {
            var ids = UnitIds.VerifiedFindings(unit.Id);
            if (ids.Count == 0 || !ids.All(byId.ContainsKey) || ids.Any(covered.Contains)) continue;
            planned.Add(PlannedUnit.Verify([.. ids.Select(id => byId[id])], members, fidelity));
            covered.UnionWith(unit.Status == UnitStatus.Done ? ids.Where(id => byId[id].Verification is not null) : ids);
        }

        var rest = findings.Where(f => !covered.Contains(f.Id)).OrderBy(f => f.Id).ToList();
        planned.AddRange(rest.Where(f => ReviewEligibility.RequiresBrowser(f.Finding, source)).Select(f => PlannedUnit.Verify(f, members, fidelity)));
        planned.AddRange(rest.Where(f => !ReviewEligibility.RequiresBrowser(f.Finding, source)).Chunk(Math.Max(1, size)).Select(chunk => PlannedUnit.Verify(chunk, members, fidelity)));
        return planned;
    }
}
