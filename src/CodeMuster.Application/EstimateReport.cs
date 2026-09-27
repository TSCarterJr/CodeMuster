using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Pending work of one unit kind.</summary>
/// <param name="Kind">The unit kind.</param>
/// <param name="Units">Units of that kind still to analyze.</param>
/// <param name="Tokens">Approximate input tokens for them, at four bytes per token.</param>
public sealed record EstimateLine(UnitKind Kind, int Units, long Tokens);

/// <summary>The API-equivalent cost of the pending work on one model (D63).</summary>
/// <param name="Model">The priced model id.</param>
/// <param name="CostUsd">Input at the uncached rate plus the assumed output at the output rate.</param>
public sealed record EstimateCost(string Model, decimal CostUsd);

/// <summary>What <see cref="Estimate"/> found still to do.</summary>
/// <param name="Lines">One line per kind that has pending work, ordered by kind.</param>
/// <param name="TotalTokens">Sum over the lines.</param>
public sealed record EstimateReport(IReadOnlyList<EstimateLine> Lines, long TotalTokens)
{
    /// <summary>Output tokens assumed for the pending work.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Output tokens per input token behind <see cref="OutputTokens"/>.</summary>
    public decimal OutputRatio { get; init; }

    /// <summary>Recorded calls the ratio was measured from, or zero when it is the documented default.</summary>
    public int MeasuredCalls { get; init; }

    /// <summary>The pending work priced per model: models already used and config prices, else the table's representative models.</summary>
    public IReadOnlyList<EstimateCost> Costs { get; init; } = [];

    /// <summary>The average recorded cost of a priced call, harness overhead included, or null before one is recorded.</summary>
    public decimal? RecordedAverageUsd { get; init; }

    /// <summary>Priced calls behind <see cref="RecordedAverageUsd"/>.</summary>
    public int RecordedCalls { get; init; }

    /// <summary>Units still to analyze.</summary>
    public int PendingUnits { get; init; }

    /// <summary>The plain-text block the CLI prints, lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var lines = Lines
            .Select(line => string.Create(CultureInfo.InvariantCulture, $"{line.Kind.ToString().ToLowerInvariant()} {line.Units} units ~{line.Tokens} tokens"))
            .Append(string.Create(CultureInfo.InvariantCulture, $"total ~{TotalTokens} tokens"))
            .ToList();
        if (Costs.Count > 0)
        {
            var basis = MeasuredCalls > 0
                ? string.Create(CultureInfo.InvariantCulture, $"measured from {MeasuredCalls} recorded calls")
                : string.Create(CultureInfo.InvariantCulture, $"the default until {Estimate.CallsForMeasuredRatio} calls with usage are recorded");
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"API-equivalent cost of ~{TotalTokens} input and ~{OutputTokens} output tokens (output assumed {OutputRatio * 100:0.#}% of input, {basis}; harness prompts and tool calls not counted):"));
            lines.AddRange(Costs.Select(cost => $"{cost.Model} ~{Spend.Money(cost.CostUsd)}"));
        }

        if (RecordedAverageUsd is { } average && PendingUnits > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"recorded calls averaged {Spend.Money(average)} each over {RecordedCalls} priced call(s), harness overhead included; {PendingUnits} pending unit(s) at that rate ~{Spend.Money(average * PendingUnits)}"));
        }

        return string.Join('\n', lines);
    }
}
