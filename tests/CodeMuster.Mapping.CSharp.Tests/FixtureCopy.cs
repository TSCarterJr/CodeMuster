namespace CodeMuster.Mapping.CSharp.Tests;

internal sealed class FixtureCopy : IDisposable
{
    public FixtureCopy(string name)
    {
        Fixtures.CopyTree(Fixtures.Root(name), Root);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "codemuster-map-" + Guid.NewGuid().ToString("N"));

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
