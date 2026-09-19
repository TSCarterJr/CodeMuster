using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerEvidenceTests
{
    private const string At = "2026-09-15T00:00:00.0000000Z";
    private static readonly Unit Review = new("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null);

    [Fact]
    public async Task Open_MigratesVersionFiveWithoutLosingAnalyses()
    {
        using var temp = new TempDirectory();
        await RawSqlite.ExecuteAsync(temp.DatabasePath, SqliteLedger.Schema + SqliteLedger.SchemaVersion2 + SqliteLedger.SchemaVersion3 + SqliteLedger.SchemaVersion4Sql + SqliteLedger.SchemaVersion5 + """
            INSERT INTO units (seq, id, kind, key, fingerprint, status, fidelity) VALUES (1, 'file:src/A.cs', 'file', 'src/A.cs', 'fp', 'done', 'full');
            INSERT INTO analyses (unit_id, fingerprint, lens_hash, created_at, succeeded, summary, agent, model, effort) VALUES ('file:src/A.cs', 'fp', 'lens', '2026-09-15T00:00:00.0000000Z', 1, 'A', 'codex', 'model', 'high');
            PRAGMA user_version = 5;
            """);

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Equal(7L, await ledger.ReadPragmaAsync("user_version", CancellationToken.None));
        Assert.Contains("evidence_json", await RawSqlite.StringsAsync(temp.DatabasePath, "SELECT name FROM pragma_table_info('analyses')"));
        Assert.Equal(1L, await RawSqlite.ScalarAsync<long>(temp.DatabasePath, "SELECT COUNT(*) FROM analyses"));
        Assert.Equal(new AgentIdentity("codex", "model", "high"), (await ledger.GetProvenanceAsync(CancellationToken.None))["file:src/A.cs"]);
        Assert.Empty(await ledger.GetLatestEvidenceAsync(CancellationToken.None));
        var analysis = Evidence();
        await ledger.RecordAnalysisAsync(analysis, [], CancellationToken.None);
        Assert.Equal(analysis, (await ledger.GetLatestEvidenceAsync(CancellationToken.None))[Review.Id]);
    }

    [Fact]
    public async Task Evidence_SurvivesReopenAndRetainsTheReviewedFingerprintAndLenses()
    {
        using var temp = new TempDirectory();
        var analysis = Evidence();
        using (var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await ledger.UpsertUnitsAsync([Review], [], CancellationToken.None);
            await ledger.RecordAnalysisAsync(analysis, [], CancellationToken.None);
            await ledger.UpsertUnitsAsync([Review with { Fingerprint = "changed", LensHash = "new-lens", Status = UnitStatus.Stale }], [], CancellationToken.None);
        }

        using var reopened = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        Assert.Equal(analysis, Assert.Single(await reopened.GetLatestEvidenceAsync(CancellationToken.None)).Value);
    }

    [Fact]
    public async Task LatestSuccessfulEvidence_UsesInsertionOrderAndIgnoresFailedAttempts()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        await ledger.UpsertUnitsAsync([Review], [], CancellationToken.None);
        var first = Evidence();
        var latest = first with { CreatedAt = "2026-09-14T00:00:00.0000000Z", Fingerprint = "new-fp", LensHash = "new-lens", EvidenceJson = "{\"screenshots\":[\"new.png\"]}" };
        await ledger.RecordAnalysisAsync(first, [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(latest, [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(first with { Succeeded = false, Summary = null, Error = "browser unavailable" }, [], CancellationToken.None);

        Assert.Equal(latest, Assert.Single(await ledger.GetLatestEvidenceAsync(CancellationToken.None)).Value);
    }

    [Fact]
    public async Task LatestSuccessWithoutEvidence_DoesNotExposeAnOlderReceipt()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        await ledger.UpsertUnitsAsync([Review], [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(Evidence(), [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(Evidence() with { EvidenceJson = null }, [], CancellationToken.None);

        Assert.Empty(await ledger.GetLatestEvidenceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Evidence_ExcludesRetiredAndMissingUnits()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var active = Review with { Id = "file:src/B.cs", Key = "src/B.cs" };
        await ledger.UpsertUnitsAsync([Review, active], [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(Evidence(), [], CancellationToken.None);
        var expected = Evidence() with { UnitId = active.Id, By = null };
        await ledger.RecordAnalysisAsync(expected, [], CancellationToken.None);
        await ledger.RecordAnalysisAsync(Evidence() with { UnitId = "missing" }, [], CancellationToken.None);
        await ledger.UpsertUnitsAsync([Review with { Status = UnitStatus.Retired }], [], CancellationToken.None);

        Assert.Equal(expected, Assert.Single(await ledger.GetLatestEvidenceAsync(CancellationToken.None)).Value);
    }

    [Fact]
    public async Task FailedEvidence_IsRetainedAsFailureHistoryWithoutBeingACompletedReceipt()
    {
        using var temp = new TempDirectory();
        var failed = Evidence() with { Succeeded = false, Summary = null, Error = "workflow not inspected" };
        using (var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await ledger.UpsertUnitsAsync([Review], [], CancellationToken.None);
            await ledger.RecordAnalysisAsync(failed, [], CancellationToken.None);
        }

        using var reopened = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        Assert.Empty(await reopened.GetLatestEvidenceAsync(CancellationToken.None));
        Assert.Equal(failed, Assert.Single(await reopened.GetFailedAnalysesAsync(CancellationToken.None)));
    }

    private static Analysis Evidence() => new(Review.Id, Review.Fingerprint, "lens", At, true, "Page inspected", null, new AgentIdentity("codex", "model", "high"))
    {
        EvidenceJson = "{\"screenshots\":[\"evidence/invoice's-view.png\"],\"notes\":\"Readable ✓\\nWorkflow checked\"}"
    };
}
