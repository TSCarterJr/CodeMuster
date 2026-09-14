using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Runs the configured repository validation independently of the fix queue.</summary>
public sealed class Validate(ITestRunner? tests)
{
    /// <summary>Returns failure when no command is configured; an empty queue is not validation evidence.</summary>
    public Task<TestRun> RunAsync(CancellationToken cancellationToken) => tests is null
        ? Task.FromResult(new TestRun(false, "no test_command configured; set it to run the repository's required build and tests"))
        : tests.RunAsync(cancellationToken);
}
