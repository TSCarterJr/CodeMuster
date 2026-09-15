namespace CodeMuster.Application;

/// <summary>Opt-in browser review settings. Include extends recognized UI conventions; exclude wins.</summary>
public sealed record UserExperienceSettings
{
    /// <summary>Whether scan plans separate browser review units for UI files.</summary>
    public bool Enabled { get; init; }

    /// <summary>Additional repo-relative UI path globs for project-specific conventions.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>UI path globs outside browser review.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>The running application address, when configured; authentication stays with the active agent.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>True for a UI source path in the enabled review scope.</summary>
    public bool Applies(string path) => Enabled && UiScope.Applies(path, Include, Exclude);
}
