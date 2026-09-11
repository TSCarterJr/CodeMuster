namespace CodeMuster.Application;

/// <summary>How a <see cref="Run"/> should behave.</summary>
/// <param name="Parallelism">Adapter calls to keep in flight at once; at least 1.</param>
/// <param name="MaxAttempts">Attempts per unit before the run gives up on it; at least 1.</param>
/// <param name="Force">Re-analyze Done units too, by marking them Stale first.</param>
public sealed record RunOptions(int Parallelism, int MaxAttempts, bool Force)
{
    /// <summary>Adapter calls to keep in flight at once.</summary>
    public int Parallelism { get; } = AtLeastOne(Parallelism, nameof(Parallelism));

    /// <summary>Attempts per unit before the run gives up on it.</summary>
    public int MaxAttempts { get; } = AtLeastOne(MaxAttempts, nameof(MaxAttempts));

    private static int AtLeastOne(int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        return value;
    }
}
