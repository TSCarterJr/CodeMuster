namespace CodeMuster.Application;

/// <summary>The proactive work an agent may perform without a separate CodeMuster request.</summary>
public enum AutomationMode
{
    /// <summary>Use CodeMuster only when explicitly requested.</summary>
    Off,
    /// <summary>Refresh repository coverage without reviewing or fixing code.</summary>
    Update,
    /// <summary>Refresh coverage and review code related to the current task.</summary>
    Review,
    /// <summary>Refresh coverage, review task-related code, and fix its confirmed findings.</summary>
    ReviewAndFix,
}
