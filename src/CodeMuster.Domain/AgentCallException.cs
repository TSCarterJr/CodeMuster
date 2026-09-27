namespace CodeMuster.Domain;

/// <summary>An agent call that failed after the harness ran, carrying whatever usage it reported, since a failed call is still paid for (D63).</summary>
public sealed class AgentCallException : InvalidOperationException
{
    /// <summary>Creates the exception with the failure message and the usage the harness reported.</summary>
    public AgentCallException(string message, AgentUsage usage, Exception? innerException = null)
        : base(message, innerException)
    {
        Usage = usage;
    }

    /// <summary>The usage the failed call reported, or <see cref="AgentUsage.Unknown"/>.</summary>
    public AgentUsage Usage { get; }
}
