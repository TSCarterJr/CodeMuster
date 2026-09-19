using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class PhysicalFileSystemTests
{
    [Fact]
    public async Task AtomicWrite_ReplacesText_AndCancellationPreservesOriginal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "codemuster-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        IFileSystem fs = new PhysicalFileSystem();
        await fs.WriteAllTextAsync(path, "original", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fs.WriteAllTextAtomicallyAsync(path, "cancelled", cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        await fs.WriteAllTextAtomicallyAsync(path, "{\"name\":\"héllo\"}", CancellationToken.None);
        Assert.Equal("{\"name\":\"héllo\"}", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task FileExists_and_ReadAllTextAsync_round_trip_in_a_temp_dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var present = Path.Combine(dir, "present.txt");
            await File.WriteAllTextAsync(present, "héllo\nworld\n");
            IFileSystem fs = new PhysicalFileSystem();

            Assert.True(fs.FileExists(present));
            Assert.False(fs.FileExists(Path.Combine(dir, "missing.txt")));
            Assert.Equal("héllo\nworld\n", await fs.ReadAllTextAsync(present, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllTextAsync_creates_parent_directories_and_writes_utf8_without_bom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "codemuster-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, ".codemuster", "config.json");
            IFileSystem fs = new PhysicalFileSystem();

            await fs.WriteAllTextAsync(path, "{ \"héllo\": 1 }\n", CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal((byte)'{', bytes[0]);
            Assert.Equal("{ \"héllo\": 1 }\n", await fs.ReadAllTextAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
