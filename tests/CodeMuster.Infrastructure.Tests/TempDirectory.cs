namespace CodeMuster.Infrastructure.Tests;

public sealed class TempDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(Root, ".codemuster", "ledger.db");

    public TempDirectory()
    {
        Directory.CreateDirectory(Root);
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
