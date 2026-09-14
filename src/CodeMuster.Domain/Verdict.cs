namespace CodeMuster.Domain;

/// <summary>What a verify unit concluded about one finding.</summary>
public enum Verdict
{
    /// <summary>The code shown supports the claim.</summary>
    Confirmed,

    /// <summary>The code shown contradicts the claim, or the defect cannot happen.</summary>
    Refuted,

    /// <summary>The code shown cannot settle the claim.</summary>
    Unsure,

    /// <summary>The previously reported defect is no longer present in current code, with resolution evidence.</summary>
    Resolved,
}
