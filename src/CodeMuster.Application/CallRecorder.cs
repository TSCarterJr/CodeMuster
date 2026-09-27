using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Records every paid agent call of one invocation with its usage and a cost fixed from the price table at record time (D63). A call counts once the harness ran: it returned a reply, or failed with an <see cref="AgentCallException"/>.</summary>
internal sealed class CallRecorder(ILedger ledger, IClock clock, Config config, AgentIdentity by, string verb)
{
    private readonly PriceTable prices = PriceTable.For(config);
    private readonly string run = verb + " " + Timestamps.Format(clock.UtcNow);

    // identity is the agent the call ran with when an engine command changed it (D65); otherwise the one this recorder was created for.
    public async Task<AgentCall> RecordAsync(string? unitId, UnitKind? kind, AgentUsage usage, bool succeeded, CancellationToken cancellationToken, AgentIdentity? identity = null)
    {
        var agent = identity ?? by;
        var (cost, source) = prices.Cost(agent, usage);
        var call = new AgentCall(Timestamps.Format(clock.UtcNow), run, unitId, kind, agent, usage, succeeded, cost, source);
        await ledger.RecordAgentCallAsync(call, cancellationToken);
        return call;
    }

    /// <summary>The usage of a failed call when the harness ran, or null when it never started, so nothing was spent.</summary>
    public static AgentUsage? PaidUsage(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AgentCallException call) return call.Usage;
        }

        return null;
    }
}
