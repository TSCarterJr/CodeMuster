namespace CodeMuster.Infrastructure;

public static class CoordinatorLock
{
    public static async Task<IDisposable> AcquireAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var common = (await GitProcess.RunAsync(repoRoot, ["rev-parse", "--path-format=absolute", "--git-common-dir"], null, cancellationToken)).Trim();
        try
        {
            return new FileStream(Path.Combine(common, "codemuster-coordinator.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("another CodeMuster command is using this repository; wait for it to finish before starting another coordinator", ex);
        }
    }
}
