namespace CodeMuster.Domain;

/// <summary>The few file operations use cases need outside the tracked tree, such as reading and writing config.</summary>
public interface IFileSystem
{
    /// <summary>True when a file exists at the absolute path.</summary>
    bool FileExists(string path);

    /// <summary>Reads a whole file as UTF-8 text.</summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    /// <summary>Reads a bounded artifact as bytes; rejects files larger than the caller's limit.</summary>
    Task<byte[]> ReadBytesAsync(string path, int maxBytes, CancellationToken cancellationToken);

    /// <summary>Writes a whole file as UTF-8 text without a byte-order mark, creating missing parent directories.</summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>Writes UTF-8 to a sibling temporary file and replaces the destination only after the write completes.</summary>
    Task WriteAllTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken);
}
