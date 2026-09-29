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

/// <summary>Findings recorded per analysed unit, from which verify calls are expected (D82).</summary>
/// <param name="PerUnit">Current findings over analysed units.</param>
/// <param name="Units">Analysed units the rate is measured over.</param>
/// <param name="CallsPerUnit">Verify calls per analysed unit once its findings are grouped into batches (D78).</param>
/// <param name="Batch">Most findings one verify call holds.</param>
public sealed record FindingRate(decimal PerUnit, int Units, decimal CallsPerUnit, int Batch);

/// <summary>The calls of one unit kind priced at a recorded cost per call (D82).</summary>
/// <param name="Kind">The unit kind.</param>
/// <param name="Pending">Units of that kind still to analyze.</param>
/// <param name="Expected">Further calls expected from findings not yet recorded; only verify has any.</param>
/// <param name="PerCallUsd">The recorded mean cost of one call.</param>
/// <param name="MeasuredFrom">Recorded calls behind that mean.</param>
/// <param name="OwnKind">True when the mean is over calls of this kind, false when it is over all the agent's calls.</param>
public sealed record EstimateCallLine(UnitKind Kind, int Pending, int Expected, decimal PerCallUsd, int MeasuredFrom, bool OwnKind)
{
    /// <summary>Pending plus expected calls.</summary>
    public int Calls => Pending + Expected;

    /// <summary>The line's calls at its cost per call.</summary>
    public decimal CostUsd => Calls * PerCallUsd;
}

/// <summary>The pending work priced per call for the agent used most recently, harness overhead included (D82).</summary>
/// <param name="Agent">The agent of the most recent priced call.</param>
/// <param name="Lines">One line per kind with calls to make, ordered by kind.</param>
/// <param name="Findings">The finding rate behind the expected verify calls, or null when verification is off or fewer than 20 units are analysed.</param>
public sealed record EstimatePerCall(string Agent, IReadOnlyList<EstimateCallLine> Lines, FindingRate? Findings);

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

    /// <summary>The pending calls at recorded costs per call, or null before a call is priced or when nothing is pending.</summary>
    public EstimatePerCall? PerCall { get; init; }

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

        if (PerCall is { } perCall)
        {
            lines.Add($"recorded cost per call with {perCall.Agent}, harness overhead included:");
            lines.AddRange(perCall.Lines.Select(line => Render(line, perCall)));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"total {perCall.Lines.Sum(line => line.Calls)} call(s) ~{Spend.Money(perCall.Lines.Sum(line => line.CostUsd))} at recorded rates"));
        }

        return string.Join('\n', lines);
    }

    private static string Render(EstimateCallLine line, EstimatePerCall perCall)
    {
        var kind = line.Kind.ToString().ToLowerInvariant();
        var expected = line.Expected > 0 && perCall.Findings is { } rate
            ? string.Create(CultureInfo.InvariantCulture, $": {line.Pending} pending and ~{line.Expected} expected at {rate.PerUnit:0.#} findings per analysed unit over {rate.Units} units{(rate.Batch > 1 ? $" in batches of up to {rate.Batch}" : "")},")
            : "";
        var basis = line.OwnKind
            ? string.Create(CultureInfo.InvariantCulture, $"the mean of {line.MeasuredFrom} {kind} calls")
            : string.Create(CultureInfo.InvariantCulture, $"the mean of all {line.MeasuredFrom} {perCall.Agent} calls (fewer than {Estimate.CallsForMeasuredRatio} {kind} calls recorded)");
        return string.Create(CultureInfo.InvariantCulture,
            $"{kind} {line.Calls} call(s){expected} at {Spend.Money(line.PerCallUsd)} each, {basis} ~{Spend.Money(line.CostUsd)}");
    }
}
