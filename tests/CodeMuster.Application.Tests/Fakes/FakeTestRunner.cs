using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeTestRunner : ITestRunner
{
    public Queue<TestRun> Results { get; } = new();
    public int Runs { get; private set; }

    public Task<TestRun> RunAsync(CancellationToken cancellationToken)
    {
        Runs++;
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new TestRun(true, ""));
    }
}
