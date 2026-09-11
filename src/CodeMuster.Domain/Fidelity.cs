namespace CodeMuster.Domain;

/// <summary>How trustworthy the mapper that built a unit was (D09).</summary>
public enum Fidelity
{
    /// <summary>Built from a compiler-backed map or from a whole file.</summary>
    Full,
    /// <summary>Built by the fallback import-graph mapper.</summary>
    Low,
}
