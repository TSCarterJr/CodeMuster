namespace CodeMuster.Application;

/// <summary>How a fix run should behave (D37, D39, D40). Parallel agents edit isolated worktrees.</summary>
/// <param name="MaxAttempts">Attempts per unit before the run gives up on it; at least 1.</param>
/// <param name="Path">Fix only files under this repo-relative folder, or anywhere when null.</param>
/// <param name="Stash">Save tracked local changes before fixing and restore them afterward.</param>
/// <param name="Parallelism">Maximum simultaneous file workers; at least one.</param>
public sealed record FixOptions(int MaxAttempts = 3, string? Path = null, bool Stash = false, int Parallelism = 1)
{
    /// <summary>Maximum simultaneous file workers.</summary>
    public int Parallelism { get; } = Parallelism >= 1
        ? Parallelism
        : throw new ArgumentOutOfRangeException(nameof(Parallelism), Parallelism, "must be at least 1");
    /// <summary>Attempts per unit before the run gives up on it.</summary>
    public int MaxAttempts { get; } = MaxAttempts >= 1
        ? MaxAttempts
        : throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "must be at least 1");
}
