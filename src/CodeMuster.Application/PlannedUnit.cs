using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>A unit as a scan plans it from the included files and the code map, before the ledger's status, lens hash, and summary are applied.</summary>
/// <param name="Id">Stable id, see <see cref="UnitIds"/>.</param>
/// <param name="Kind">The unit kind.</param>
/// <param name="Key">A path for file and orphan units, the entry point display for slices.</param>
/// <param name="Fidelity">Trust in the map that built it.</param>
/// <param name="Members">The files or symbols the unit covers, each carrying this unit's id.</param>
public sealed record PlannedUnit(string Id, UnitKind Kind, string Key, Fidelity Fidelity, IReadOnlyList<UnitMember> Members);
