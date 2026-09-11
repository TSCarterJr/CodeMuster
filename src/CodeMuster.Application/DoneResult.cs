namespace CodeMuster.Application;

/// <summary>What <see cref="Done"/> did with a response.</summary>
public enum DoneOutcome
{
    /// <summary>The analysis and its findings were stored and the unit is Done.</summary>
    Recorded,

    /// <summary>Nothing was stored: the unit is unknown, changed since next, or a finding cites a path outside it.</summary>
    Rejected,

    /// <summary>The response was not valid JSON for the schema; a failed analysis was stored and the unit is Failed (D10).</summary>
    InvalidResponse,
}

/// <summary>The outcome of <see cref="Done"/> and a one-line message for the console.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">Why, in one line.</param>
public sealed record DoneResult(DoneOutcome Outcome, string Message);
