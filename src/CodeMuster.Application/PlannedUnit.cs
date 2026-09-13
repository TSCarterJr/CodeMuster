using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>A unit as a scan plans it from the included files and the code map, before the ledger's status, lens hash, and summary are applied.</summary>
/// <param name="Id">Stable id, see <see cref="UnitIds"/>.</param>
/// <param name="Kind">The unit kind.</param>
/// <param name="Key">A path for file and orphan units, the entry point display for slices, the cited lines for verify units.</param>
/// <param name="Fidelity">Trust in the map that built it.</param>
/// <param name="Members">The files or symbols the unit covers, each carrying this unit's id.</param>
public sealed record PlannedUnit(string Id, UnitKind Kind, string Key, Fidelity Fidelity, IReadOnlyList<UnitMember> Members)
{
    /// <summary>The dependency unit for one manifest: the manifest as it stands, so a change to it re-audits (D38).</summary>
    public static PlannedUnit Dependency(string manifest, string contentHash)
    {
        var id = UnitIds.Dependency(manifest);
        return new PlannedUnit(id, UnitKind.Dependency, manifest, Fidelity.Full, [new UnitMember(id, manifest, null, contentHash, 0)]);
    }

    /// <summary>The fix unit for one file: its whole content, so a change to the file marks the unit stale (D37).</summary>
    public static PlannedUnit Fix(string path, string contentHash)
    {
        var id = UnitIds.Fix(path);
        return new PlannedUnit(id, UnitKind.Fix, path, Fidelity.Full, [new UnitMember(id, path, null, contentHash, 0)]);
    }

    /// <summary>The verify unit for <paramref name="finding"/>: the members of the unit that reported it, so it goes stale with that code, keyed by the lines the finding cites.</summary>
    public static PlannedUnit Verify(UnitFinding finding, IReadOnlyList<UnitMember> members, Fidelity fidelity)
    {
        var id = UnitIds.Verify(finding.Id);
        return new PlannedUnit(id, UnitKind.Verify, FindingLocation.Of(finding.Finding), fidelity, members.Select(m => m with { UnitId = id }).ToList());
    }
}
