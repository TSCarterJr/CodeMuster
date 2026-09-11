using System.Globalization;

namespace CodeMuster.Application;

/// <summary>Where the audit stands, as <see cref="Status"/> reads it from the ledger.</summary>
/// <param name="HeadCommit">Commit of the last scan, or null before any scan.</param>
/// <param name="Analyzed">Units that are Done.</param>
/// <param name="Total">Units that are not Retired.</param>
/// <param name="Stale">Units that are Stale.</param>
/// <param name="Excluded">Present files recorded with an exclusion reason.</param>
/// <param name="LowFidelity">Non-retired units built by a low-fidelity map (D09).</param>
/// <param name="ResolutionRate">Mapper resolution rate of the last scan, 0..1, or null when no mapper ran.</param>
public sealed record StatusReport(string? HeadCommit, int Analyzed, int Total, int Stale, int Excluded, int LowFidelity, double? ResolutionRate)
{
    /// <summary>The plain-text block the CLI prints, lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var at = HeadCommit is null ? "no scan yet" : HeadCommit[..Math.Min(7, HeadCommit.Length)];
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"analyzed {Analyzed}/{Total} at {at}"),
            string.Create(CultureInfo.InvariantCulture, $"stale {Stale}"),
            string.Create(CultureInfo.InvariantCulture, $"excluded {Excluded}"),
            string.Create(CultureInfo.InvariantCulture, $"low-fidelity {LowFidelity}"),
        };
        if (ResolutionRate is { } rate)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"resolution {rate * 100:0.0}%"));
        }

        return string.Join('\n', lines);
    }
}
