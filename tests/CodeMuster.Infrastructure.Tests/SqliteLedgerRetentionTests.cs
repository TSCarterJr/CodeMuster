using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerRetentionTests
{
    private const string At = "2026-09-10T04:00:00.0000000Z";

    [Fact]
    public async Task DeletedFile_StaysInFilesWithItsDeletedAtAcrossLaterScans()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var file = new FileRecord("src/Gone.cs", "csharp", "hash-gone", 1, At, At, At, "abc123", At, null, null, "Summary", "hash-gone");
        await ledger.UpsertFilesAsync([file], CancellationToken.None);

        var deleted = file with { DeletedAt = At };
        await ledger.UpsertFilesAsync([deleted], CancellationToken.None);
        Assert.Equal([deleted], await ledger.GetFilesAsync(CancellationToken.None));

        await ledger.UpsertFilesAsync([file with { Path = "src/New.cs" }], CancellationToken.None);
        Assert.Equal(2, (await ledger.GetFilesAsync(CancellationToken.None)).Count);
        Assert.Contains(deleted, await ledger.GetFilesAsync(CancellationToken.None));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM files WHERE deleted_at IS NOT NULL"));
    }

    [Fact]
    public async Task RetiredUnit_IsNeverHandedOutButKeepsItsRowMembersAndHistory()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var unit = new Unit(UnitIds.File("src/Gone.cs"), UnitKind.File, "src/Gone.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null);
        var member = new UnitMember(unit.Id, "src/Gone.cs", null, "hash-gone", 0);
        await ledger.UpsertUnitsAsync([unit], [member], CancellationToken.None);
        var finding = new Finding("src/Gone.cs", 1, 1, Severity.Low, "style", "claim", "evidence", 0.4, "lens");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, "fp", "lens", At, true, "Summary", null), [finding], CancellationToken.None);

        var retired = unit with { Status = UnitStatus.Retired, Summary = "Summary", SummaryHash = "fp", LensHash = "lens" };
        await ledger.UpsertUnitsAsync([retired], [member], CancellationToken.None);

        Assert.Empty(await ledger.NextAsync(10, CancellationToken.None));
        Assert.Equal([retired], await ledger.GetUnitsAsync(CancellationToken.None));
        Assert.Equal(retired, await ledger.GetUnitAsync(unit.Id, CancellationToken.None));
        Assert.Equal([member], await ledger.GetMembersAsync([unit.Id], CancellationToken.None));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM analyses"));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM findings"));
    }

    [Fact]
    public async Task RetiredUnit_ReturnsToTheQueueInItsOriginalPositionWhenReUpsertedAsPending()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var first = new Unit(UnitIds.File("src/First.cs"), UnitKind.File, "src/First.cs", "fp-1", UnitStatus.Pending, Fidelity.Full, null, null, null);
        var second = first with { Id = UnitIds.File("src/Second.cs"), Key = "src/Second.cs", Fingerprint = "fp-2" };
        await ledger.UpsertUnitsAsync([first, second], [], CancellationToken.None);
        await ledger.UpsertUnitsAsync([first with { Status = UnitStatus.Retired }], [], CancellationToken.None);
        Assert.Equal([second], await ledger.NextAsync(10, CancellationToken.None));

        await ledger.UpsertUnitsAsync([first], [], CancellationToken.None);

        Assert.Equal([first, second], await ledger.NextAsync(10, CancellationToken.None));
    }
}
