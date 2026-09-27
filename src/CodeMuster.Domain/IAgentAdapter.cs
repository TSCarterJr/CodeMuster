namespace CodeMuster.Domain;

/// <summary>Runs one coding-agent harness headlessly on one unit pack (D10).</summary>
public interface IAgentAdapter
{
    /// <summary>The harness, model, and effort this adapter runs with, recorded on every analysis it produces (D35).</summary>
    AgentIdentity Identity { get; }

    /// <summary>Sends the pack and returns the response text with the call's usage (D63). A call that fails after the harness ran throws <see cref="AgentCallException"/> with the usage it reported.</summary>
    Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken);
}
