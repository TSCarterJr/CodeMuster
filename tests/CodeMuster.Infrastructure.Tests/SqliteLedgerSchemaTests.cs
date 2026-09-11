using CodeMuster.Domain;
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
    public async Task Open_TwiceIsIdempotentAndLeavesUserVersionAtTwo()
    {
        using var temp = new TempDirectory();
        using (await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
        }

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal(2L, await ledger.ReadPragmaAsync("user_version", CancellationToken.None));
        Assert.Equal(2L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(["analyses", "files", "findings", "runs", "unit_members", "units"], await RawSqlite.StringsAsync(temp.DatabasePath, Tables));
    }

    [Fact]
    public async Task Open_MigratesAVersionOneLedgerAndKeepsItsRows()
    {
        using var temp = new TempDirectory();
        await RawSqlite.ExecuteAsync(temp.DatabasePath, SqliteLedger.Schema + """
            INSERT INTO unit_members (unit_id, path, symbol, member_hash, distance) VALUES ('file:src/A.cs', 'src/A.cs', NULL, 'h1', 0);
            INSERT INTO runs (started_at, head_commit, files_included, files_excluded, units_total, resolution_rate) VALUES ('2026-09-10T00:00:00.0000000Z', 'aaa111', 1, 0, 1, NULL);
            PRAGMA user_version = 1;
            """);

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal(2L, await ledger.ReadPragmaAsync("user_version", CancellationToken.None));
        Assert.Equal([new UnitMember("file:src/A.cs", "src/A.cs", null, "h1", 0)], await ledger.GetMembersAsync(["file:src/A.cs"], CancellationToken.None));
        var run = await ledger.GetLastRunAsync(CancellationToken.None);
        Assert.NotNull(run);
        Assert.Null(run.TopUnresolvedNames);
    }

    [Fact]
    public async Task Open_RefusesALedgerWrittenByANewerVersion()
    {
        using var temp = new TempDirectory();
        await RawSqlite.ExecuteAsync(temp.DatabasePath, "PRAGMA user_version = 3");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None));

        Assert.Contains("newer codemuster", error.Message);
        Assert.Equal(3L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "PRAGMA user_version"));
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
