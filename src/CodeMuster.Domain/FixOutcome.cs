namespace CodeMuster.Domain;

/// <summary>What fix mode did about one finding (D37).</summary>
public enum FixState
{
    /// <summary>The code was changed to address it.</summary>
    Fixed,

    /// <summary>It was left alone, with a reason.</summary>
    Declined,
}

/// <summary>How one finding fared in fix mode, and why.</summary>
/// <param name="State">Fixed or declined.</param>
/// <param name="Reason">What was changed, or why it was left alone.</param>
public sealed record FixOutcome(FixState State, string Reason);
