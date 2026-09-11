namespace CodeMuster.Domain;

/// <summary>The current time, so use cases stay deterministic under test.</summary>
public interface IClock
{
    /// <summary>Now, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
