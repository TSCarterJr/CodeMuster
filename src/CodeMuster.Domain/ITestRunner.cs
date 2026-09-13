namespace CodeMuster.Domain;

/// <summary>How the repository's own test command went (D37).</summary>
/// <param name="Passed">True when the command exited zero.</param>
/// <param name="Output">What it printed, kept for the reason a fix was thrown away.</param>
public sealed record TestRun(bool Passed, string Output);

/// <summary>Runs the repository's own test command, so a fix that breaks the build never counts as done (D37).</summary>
public interface ITestRunner
{
    /// <summary>Runs the command and reports how it went; never throws for a failing suite.</summary>
    Task<TestRun> RunAsync(CancellationToken cancellationToken);
}
