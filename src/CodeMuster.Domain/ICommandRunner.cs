namespace CodeMuster.Domain;

/// <summary>How one command went.</summary>
/// <param name="ExitCode">Its exit code, or -1 when it could not start.</param>
/// <param name="Output">What it printed on standard output.</param>
/// <param name="Error">What it printed on standard error, or why it could not start.</param>
public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    /// <summary>True when the command exited zero.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs a program found on PATH with an argument list, never through a shell, such as the commands <c>doctor --fix</c> offers (D70).</summary>
public interface ICommandRunner
{
    /// <summary>True when the program can be found on PATH.</summary>
    bool IsOnPath(string executable);

    /// <summary>Runs <paramref name="command"/> (the program, then its arguments) in the absolute <paramref name="workingDirectory"/>; never throws for a failing or missing program.</summary>
    Task<CommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> command, CancellationToken cancellationToken);
}
