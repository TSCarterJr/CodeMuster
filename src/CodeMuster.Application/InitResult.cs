namespace CodeMuster.Application;

/// <summary>What <c>init</c> did to <c>.gitignore</c>.</summary>
public enum GitignoreOutcome
{
    /// <summary>The ledger was already ignored; the file was not touched.</summary>
    AlreadyCovered,
    /// <summary>The ledger lines were appended.</summary>
    Appended,
    /// <summary>The user or a flag declined; the file was not touched.</summary>
    Skipped,
}

/// <summary>What <c>init</c> did.</summary>
/// <param name="ConfigCreated">True when <c>.codemuster/config.json</c> was written; false when it already existed.</param>
/// <param name="Gitignore">What happened to <c>.gitignore</c>.</param>
public sealed record InitResult(bool ConfigCreated, GitignoreOutcome Gitignore);
