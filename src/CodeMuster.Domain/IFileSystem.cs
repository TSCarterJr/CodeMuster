namespace CodeMuster.Domain;

/// <summary>The few file operations use cases need outside the tracked tree, such as reading config.</summary>
public interface IFileSystem
{
    /// <summary>True when a file exists at the absolute path.</summary>
    bool FileExists(string path);

    /// <summary>Reads a whole file as UTF-8 text.</summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
}
