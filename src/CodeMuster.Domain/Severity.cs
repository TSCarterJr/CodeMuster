namespace CodeMuster.Domain;

/// <summary>How serious a finding is, from most to least.</summary>
public enum Severity
{
    /// <summary>Exploitable or data-losing today; fix before anything else.</summary>
    Critical,

    /// <summary>A real defect with significant impact.</summary>
    High,

    /// <summary>A real defect with limited impact.</summary>
    Medium,

    /// <summary>Minor issue or hardening opportunity.</summary>
    Low,

    /// <summary>Observation with no defect behind it.</summary>
    Info,
}
