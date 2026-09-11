using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerFileTests
{
    private static readonly FileRecord Full = new(
        "src/A.cs", "csharp", "hash-a", 10, "2026-09-10T00:00:00.0000000Z", "2026-09-10T00:00:01.0000000Z", "2026-09-10T00:00:02.0000000Z",
        "abc123", "2026-09-09T00:00:00.0000000Z", "generated", "2026-09-10T01:00:00.0000000Z", "Summary of A", "hash-a");

    private static readonly FileRecord Sparse = new(
        "src/B.cs", "typescript", "hash-b", 0, "2026-09-10T00:00:00.0000000Z", "2026-09-10T00:00:00.0000000Z", "2026-09-10T00:00:00.0000000Z",
        null, null, null, null, null, null);

    [Fact]
    public async Task GetFiles_IsEmptyOnAFreshLedger()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        await ledger.UpsertFilesAsync([], CancellationToken.None);

        Assert.Empty(await ledger.GetFilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpsertFiles_InsertsAndRoundTripsEveryColumnAsNullAndAsValue()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        await ledger.UpsertFilesAsync([Full, Sparse], CancellationToken.None);

        Assert.Equal([Full, Sparse], await ledger.GetFilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpsertFiles_WithSamePathReplacesEveryColumn()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        await ledger.UpsertFilesAsync([Full], CancellationToken.None);

        var cleared = Sparse with { Path = Full.Path };
        await ledger.UpsertFilesAsync([cleared], CancellationToken.None);
        Assert.Equal([cleared], await ledger.GetFilesAsync(CancellationToken.None));

        await ledger.UpsertFilesAsync([Full], CancellationToken.None);
        Assert.Equal([Full], await ledger.GetFilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetFiles_ReturnsExcludedAndDeletedRowsToo()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var excluded = Sparse with { Path = "vendor/lib.js", ExcludedReason = "vendored" };
        var deleted = Sparse with { Path = "src/Gone.cs", DeletedAt = "2026-09-10T02:00:00.0000000Z" };

        await ledger.UpsertFilesAsync([Full, excluded, deleted], CancellationToken.None);

        var files = await ledger.GetFilesAsync(CancellationToken.None);
        Assert.Equal(3, files.Count);
        Assert.Contains(excluded, files);
        Assert.Contains(deleted, files);
    }

    [Fact]
    public async Task UpsertFiles_PersistsAcrossReopen()
    {
        using var temp = new TempDirectory();
        using (var first = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await first.UpsertFilesAsync([Full], CancellationToken.None);
        }

        using var second = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal([Full], await second.GetFilesAsync(CancellationToken.None));
    }
}
