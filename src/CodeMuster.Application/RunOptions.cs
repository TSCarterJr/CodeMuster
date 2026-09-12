using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>How a <see cref="Run"/> should behave.</summary>
/// <param name="Parallelism">Adapter calls to keep in flight at once; at least 1.</param>
/// <param name="MaxAttempts">Attempts per unit before the run gives up on it; at least 1.</param>
/// <param name="Force">Re-analyze Done units too, by marking them Stale first.</param>
/// <param name="Kind">Work only units of this kind, or every kind when null.</param>
/// <param name="Path">Work only units with a member under this repo-relative folder, or anywhere when null.</param>
public sealed record RunOptions(int Parallelism, int MaxAttempts, bool Force, UnitKind? Kind = null, string? Path = null)
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
