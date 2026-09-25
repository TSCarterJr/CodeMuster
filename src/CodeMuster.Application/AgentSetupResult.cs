namespace CodeMuster.Application;

/// <summary>What <see cref="AgentSetup.InstallAsync"/> did for one agent.</summary>
/// <param name="Agent">The agent.</param>
/// <param name="SkillWritten">Whether its project skill was written because it was missing or out of date.</param>
/// <param name="Hook">What happened to its change hook.</param>
/// <param name="SettingsPath">The settings file that holds the hook; null when hooks were not requested.</param>
/// <param name="RewroteSettings">Whether an existing settings file was rewritten, which drops its comments and formatting.</param>
public sealed record AgentSetupResult(string Agent, bool SkillWritten, HookChange Hook, string? SettingsPath, bool RewroteSettings);

/// <summary>What setup did to an agent's CodeMuster change hook.</summary>
public enum HookChange
{
    /// <summary>Hooks were not requested.</summary>
    None,

    /// <summary>No CodeMuster hook existed, so one was added.</summary>
    Added,

    /// <summary>An existing CodeMuster hook had an outdated matcher or timeout, or shared an entry with another hook, and was repaired in place.</summary>
    Repaired,

    /// <summary>The CodeMuster hook was already current and the settings file was left alone.</summary>
    Current,
}
