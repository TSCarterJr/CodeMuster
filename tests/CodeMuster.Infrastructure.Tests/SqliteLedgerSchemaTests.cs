using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerSchemaTests
{
    private const string Tables = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

    [Fact]
    public async Task Open_CreatesParentDirectoryFileAndAllTables()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.True(File.Exists(temp.DatabasePath));
        Assert.Equal(["analyses", "files", "findings", "runs", "unit_members", "units"], await RawSqlite.StringsAsync(temp.DatabasePath, Tables));
    }

    [Fact]
    public async Task Open_TwiceIsIdempotentAndLeavesUserVersionAtOne()
    {
        using var temp = new TempDirectory();
        using (await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
        }

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal(1L, await ledger.ReadPragmaAsync("user_version", CancellationToken.None));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(["analyses", "files", "findings", "runs", "unit_members", "units"], await RawSqlite.StringsAsync(temp.DatabasePath, Tables));
    }

    [Fact]
    public async Task Open_SetsBusyTimeoutAndWriteAheadLogging()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal(5000L, await ledger.ReadPragmaAsync("busy_timeout", CancellationToken.None));
        Assert.Equal("wal", await RawSqlite.ScalarAsync<string>(temp.DatabasePath, "PRAGMA journal_mode"));
    }

    [Fact]
    public async Task Dispose_ReleasesTheFile()
    {
        using var temp = new TempDirectory();
        var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        ledger.Dispose();

        File.Delete(temp.DatabasePath);
        Assert.False(File.Exists(temp.DatabasePath));
    }
}
