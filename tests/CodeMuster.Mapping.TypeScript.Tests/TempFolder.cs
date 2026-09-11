namespace CodeMuster.Mapping.TypeScript.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("codemuster-ts-tests-").FullName;

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Copy(string source, string relativeTarget, params string[] skipped) =>
        CopyDirectory(source, Path.Combine(Root, relativeTarget), skipped);

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

    private static void CopyDirectory(string source, string target, string[] skipped)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source).Where(directory => !skipped.Contains(Path.GetFileName(directory))))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), skipped);
        }
    }
}
