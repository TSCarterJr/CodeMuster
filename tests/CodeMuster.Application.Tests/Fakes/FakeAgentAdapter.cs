using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeAgentAdapter(Func<string, CancellationToken, Task<string>> respond) : IAgentAdapter
{
    private readonly List<(int Count, TaskCompletionSource Signal)> waiters = [];
    private int inFlight;
    private int maxInFlight;

    public List<string> Packs { get; } = [];
    public int MaxInFlight => Volatile.Read(ref maxInFlight);
    public TaskCompletionSource? Gate { get; set; }
    public AgentIdentity Identity { get; set; } = new("fake");

    public async Task<string> RunAsync(string pack, CancellationToken cancellationToken)
    {
        Enter(pack);
        try
        {
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return await respond(pack, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    public Task WhenInFlightAsync(int count)
    {
        lock (waiters)
        {
            if (Volatile.Read(ref inFlight) >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((count, signal));
            return signal.Task;
        }
    }

    private void Enter(string pack)
    {
        var now = Interlocked.Increment(ref inFlight);
        lock (waiters)
        {
            Packs.Add(pack);
            maxInFlight = Math.Max(maxInFlight, now);
            foreach (var waiter in waiters.Where(w => w.Count <= now).ToList())
            {
                waiter.Signal.SetResult();
                waiters.Remove(waiter);
            }
        }
    }
}
