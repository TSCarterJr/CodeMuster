namespace CodeMuster.Domain;

/// <summary>One unit of analysis work, of any kind (D06).</summary>
/// <param name="Id">Stable id, see <see cref="UnitIds"/>.</param>
/// <param name="Kind">The unit kind.</param>
/// <param name="Key">What the unit is about: a path for file and orphan units, the entry point display for slices.</param>
/// <param name="Fingerprint">Hash over the members, see <see cref="Fingerprints"/>.</param>
/// <param name="Status">Where the unit stands in the loop.</param>
/// <param name="Fidelity">Trust in the map that built it.</param>
/// <param name="LensHash">Hash of the lenses the last successful analysis used, or null.</param>
/// <param name="Summary">One-line summary from the last successful analysis.</param>
/// <param name="SummaryHash">Fingerprint the summary was generated from.</param>
public sealed record Unit(
    string Id,
    UnitKind Kind,
    string Key,
    string Fingerprint,
    UnitStatus Status,
    Fidelity Fidelity,
    string? LensHash,
    string? Summary,
    string? SummaryHash);
