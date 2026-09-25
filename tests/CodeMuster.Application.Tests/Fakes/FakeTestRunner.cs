using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeTestRunner : ITestRunner
{
    public Queue<TestRun> Results { get; } = new();
    public int Runs { get; private set; }
    public Action? OnRun { get; set; }

    public Task<TestRun> RunAsync(CancellationToken cancellationToken)
    {
        Runs++;
        OnRun?.Invoke();
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new TestRun(true, ""));
    }
}
