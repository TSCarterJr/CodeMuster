namespace CodeMuster.Domain;

/// <summary>Where a unit stands in the loop.</summary>
public enum UnitStatus
{
    /// <summary>Never analyzed.</summary>
    Pending,
    /// <summary>Analyzed at its current fingerprint and lens.</summary>
    Done,
    /// <summary>Analyzed once, but a member or its lens changed since.</summary>
    Stale,
    /// <summary>The last analysis attempt produced an invalid response.</summary>
    Failed,
    /// <summary>No longer part of the tree; kept for history, never handed out.</summary>
    Retired,
}
