using System.Globalization;

namespace CodeMuster.Application;

/// <summary>A step of <c>codemuster auto</c>, in the order the flow runs them (D85).</summary>
public enum AutoStep
{
    /// <summary>Check the SDKs, node and agents the repository needs.</summary>
    Doctor,

    /// <summary>Map the repository and plan units.</summary>
    Scan,

    /// <summary>Show the calls and cost, and ask before spending.</summary>
    Estimate,

    /// <summary>Audit the pending units.</summary>
    Run,

    /// <summary>Check each finding.</summary>
    Verify,

    /// <summary>Print the findings.</summary>
    Report,

    /// <summary>Repair confirmed findings, one local commit per file.</summary>
    Fix,

    /// <summary>Run the repository's test command on the final tree.</summary>
    Validate,
}

/// <summary>Chooses and checks the steps of <c>codemuster auto</c> (D85).</summary>
public static class AutoSteps
{
    /// <summary>The npm launcher's step, named in <c>--steps</c> and <c>--skip</c> but never run by the CLI itself.</summary>
    public const string Update = "update";

    /// <summary>Every step, in flow order.</summary>
    public static readonly IReadOnlyList<AutoStep> All = Enum.GetValues<AutoStep>();

    /// <summary>What <c>auto</c> runs unless told otherwise: every step except fix and validate, since fix writes commits.</summary>
    public static readonly IReadOnlyList<AutoStep> Defaults = [.. All.Where(step => step is not (AutoStep.Fix or AutoStep.Validate))];

    /// <summary>The steps named by <paramref name="steps"/>, or the defaults, less those named by <paramref name="skip"/>, in flow order; both are comma-separated names, and <c>update</c> is accepted and left to the launcher.</summary>
    public static IReadOnlyList<AutoStep> Select(string? steps, string? skip)
    {
        var chosen = steps is null ? Defaults : Parse(steps);
        var skipped = skip is null ? [] : Parse(skip);
        return [.. All.Where(step => chosen.Contains(step) && !skipped.Contains(step))];
    }

    /// <summary>True when a step calls an agent.</summary>
    public static bool NeedsAgent(IEnumerable<AutoStep> steps) => steps.Any(step => step is AutoStep.Run or AutoStep.Verify or AutoStep.Fix);

    /// <summary>A warning when fix is chosen without verify, since fix repairs only confirmed findings (D37); null otherwise.</summary>
    public static string? FixWarning(IReadOnlyCollection<AutoStep> steps, int confirmed) =>
        !steps.Contains(AutoStep.Fix) || steps.Contains(AutoStep.Verify) ? null
        : "fix repairs only confirmed findings, and verify is not selected: " + (confirmed == 0
            ? "0 findings are confirmed, so fix has nothing to repair."
            : string.Create(CultureInfo.InvariantCulture, $"fix will repair the {confirmed} confirmed now; new findings wait for verify."));

    /// <summary>The lowercase name of a step.</summary>
    public static string Name(AutoStep step) => step.ToString().ToLowerInvariant();

    private static List<AutoStep> Parse(string names)
    {
        var steps = new List<AutoStep>();
        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(name, Update, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Enum.TryParse<AutoStep>(name, ignoreCase: true, out var step) || !Enum.IsDefined(step) || int.TryParse(name, out _))
                throw new ArgumentException($"unknown step \"{name}\"; steps are {Update}, {string.Join(", ", All.Select(Name))}");
            steps.Add(step);
        }

        return steps;
    }
}
