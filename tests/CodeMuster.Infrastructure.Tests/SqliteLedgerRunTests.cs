using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerRunTests
{
    [Fact]
    public async Task GetLastRun_IsNullBeforeTheFirstScan()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Null(await ledger.GetLastRunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordRun_AppendsAndGetLastRunReturnsTheNewest()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var first = new Run("2026-09-10T00:00:00.0000000Z", "aaa111", 10, 2, 8, null);
        var second = new Run("2026-09-10T01:00:00.0000000Z", "bbb222", 11, 2, 9, 0.95);

        await ledger.RecordRunAsync(first, CancellationToken.None);
        await ledger.RecordRunAsync(second, CancellationToken.None);

        Assert.Equal(second, await ledger.GetLastRunAsync(CancellationToken.None));
        Assert.Equal(2L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM runs"));
    }

    [Fact]
    public async Task RecordRun_RoundTripsANullResolutionRate()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var run = new Run("2026-09-10T00:00:00.0000000Z", "aaa111", 1, 0, 1, null);

        await ledger.RecordRunAsync(run, CancellationToken.None);

        Assert.Equal(run, await ledger.GetLastRunAsync(CancellationToken.None));
    }
}
