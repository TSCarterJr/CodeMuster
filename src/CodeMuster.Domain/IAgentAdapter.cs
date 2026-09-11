namespace CodeMuster.Domain;

/// <summary>Runs one coding-agent harness headlessly on one unit pack (D10).</summary>
public interface IAgentAdapter
{
    /// <summary>Sends the pack and returns the raw text the agent printed.</summary>
    Task<string> RunAsync(string pack, CancellationToken cancellationToken);
}
