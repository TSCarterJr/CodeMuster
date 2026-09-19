using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Where the units of one kind stand.</summary>
/// <param name="Kind">The unit kind.</param>
/// <param name="Done">Units of that kind that are Done.</param>
/// <param name="Total">Units of that kind that are not Retired.</param>
/// <param name="Stale">Units of that kind that are Stale.</param>
public sealed record KindStatus(UnitKind Kind, int Done, int Total, int Stale);

/// <summary>Where the audit stands, as <see cref="Status"/> reads it from the ledger.</summary>
/// <param name="HeadCommit">Commit of the last scan, or null before any scan.</param>
/// <param name="Analyzed">Units that are Done.</param>
/// <param name="Total">Units that are not Retired.</param>
/// <param name="Stale">Units that are Stale.</param>
/// <param name="Excluded">Present files recorded with an exclusion reason.</param>
/// <param name="LowFidelity">Non-retired units built by a low-fidelity map (D09).</param>
/// <param name="ResolutionRate">Mapper resolution rate of the last scan, 0..1, or null when no mapper ran.</param>
/// <param name="Kinds">Counts per unit kind present, in kind order.</param>
/// <param name="ResolutionThreshold">The rate below which coverage is never complete (D09).</param>
/// <param name="TopUnresolvedNames">Most frequent unresolved call names of the last scan, or null when no mapper ran.</param>
/// <param name="VulnerablePackages">Outstanding vulnerable packages by severity, from the last dependency audit (D38).</param>
public sealed record StatusReport(
    string? HeadCommit,
    int Analyzed,
    int Total,
    int Stale,
    int Excluded,
    int LowFidelity,
    double? ResolutionRate,
    IReadOnlyList<KindStatus> Kinds,
    double ResolutionThreshold,
    IReadOnlyList<string>? TopUnresolvedNames,
    IReadOnlyDictionary<Severity, int>? VulnerablePackages = null)
{
    /// <summary>Units skipped without being analyzed because their pack exceeded the budget.</summary>
    public int Skipped { get; init; }

    /// <summary>Browser review applicability and recorded evidence, separate from code coverage.</summary>
    public string? UxStatus { get; init; }

    /// <summary>Whether required browser work lacks a current recorded receipt.</summary>
    public bool UxIncomplete { get; init; }

    /// <summary>The plain-text block the CLI prints, lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var at = HeadCommit is null ? "no scan yet" : HeadCommit[..Math.Min(7, HeadCommit.Length)];
        var lines = new List<string> { string.Create(CultureInfo.InvariantCulture, $"analyzed {Analyzed}/{Total} at {at}") };
        foreach (var kind in Kinds)
        {
            var name = kind.Kind.ToString().ToLowerInvariant();
            lines.Add(kind.Stale == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{name} {kind.Done}/{kind.Total}")
                : string.Create(CultureInfo.InvariantCulture, $"{name} {kind.Done}/{kind.Total}, {kind.Stale} stale"));
        }

        if (VulnerablePackages is { Count: > 0 } vulnerable)
        {
            var byWorst = vulnerable
                .OrderBy(entry => entry.Key)
                .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Value} {entry.Key.ToString().ToLowerInvariant()}"));
            lines.Add("vulnerable packages " + string.Join(", ", byWorst));
        }

        if (Skipped > 0) lines.Add(string.Create(CultureInfo.InvariantCulture, $"skipped {Skipped}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"stale {Stale}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"excluded {Excluded}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"low-fidelity {LowFidelity}"));
        if (UxStatus is not null) lines.Add(UxStatus);
        if (ResolutionRate is { } rate)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"resolution {rate * 100:0.0}%"));
        }

        if (ResolutionRate is { } low && low < ResolutionThreshold)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"incomplete: resolution {low * 100:0.0}% is below the {ResolutionThreshold * 100:0.0}% threshold"));
            if (TopUnresolvedNames is { Count: > 0 } names)
            {
                lines.Add("top unresolved: " + string.Join(", ", names));
            }
        }
        else if (LowFidelity > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"incomplete: {LowFidelity} unit(s) have no call map because their language's mapper failed"));
        }
        else if (UxIncomplete)
        {
            lines.Add("incomplete: browser readability and workflow evidence is still required");
        }
        else if (Total > 0 && Analyzed == Total)
        {
            lines.Add("complete");
        }

        return string.Join('\n', lines);
    }
}
