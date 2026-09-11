using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Pending work of one unit kind.</summary>
/// <param name="Kind">The unit kind.</param>
/// <param name="Units">Units of that kind still to analyze.</param>
/// <param name="Tokens">Approximate input tokens for them, at four bytes per token.</param>
public sealed record EstimateLine(UnitKind Kind, int Units, long Tokens);

/// <summary>What <see cref="Estimate"/> found still to do.</summary>
/// <param name="Lines">One line per kind that has pending work, ordered by kind.</param>
/// <param name="TotalTokens">Sum over the lines.</param>
public sealed record EstimateReport(IReadOnlyList<EstimateLine> Lines, long TotalTokens)
{
    /// <summary>The plain-text block the CLI prints, lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var lines = Lines
            .Select(line => string.Create(CultureInfo.InvariantCulture, $"{line.Kind.ToString().ToLowerInvariant()} {line.Units} units ~{line.Tokens} tokens"))
            .Append(string.Create(CultureInfo.InvariantCulture, $"total ~{TotalTokens} tokens"));
        return string.Join('\n', lines);
    }
}
