using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerSpendTests
{
    private const string Tables = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

    [Fact]
    public async Task AgentCalls_SurviveReopen_InOrder_WithExactCostsAndUnknownFieldsKeptNull()
    {
        using var temp = new TempDirectory();
        var priced = new AgentCall("2026-09-27T10:00:00.0000000Z", "run 2026-09-27T09:59:00.0000000Z", "file:src/a.cs", UnitKind.File,
            new AgentIdentity("claude", "opus", "high"), new AgentUsage(1200, 340, 50000, 2100, "claude-opus-4-8", 0.123456789m) { CacheWrite1hTokens = 2000 }, true, 0.123456789m, "harness");
        var failed = new AgentCall("2026-09-27T10:01:00.0000000Z", "fix 2026-09-27T10:00:30.0000000Z", "fix:src/a.cs", UnitKind.Fix,
            new AgentIdentity("codex"), AgentUsage.Unknown, false, null, null);
        var outside = new AgentCall("2026-09-27T10:02:00.0000000Z", "intelligent-config 2026-09-27T10:02:00.0000000Z", null, null,
            new AgentIdentity("gemini", "gemini-2.5-pro"), new AgentUsage(10, 20, null, null, "gemini-2.5-pro", null), true, 0.0000725m, "table 2026-09-27");
        using (var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None))
        {
            await ledger.RecordAgentCallAsync(priced, CancellationToken.None);
            await ledger.RecordAgentCallAsync(failed, CancellationToken.None);
            await ledger.RecordAgentCallAsync(outside, CancellationToken.None);
        }

        using var reopened = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        Assert.Equal([priced, failed, outside], await reopened.GetAgentCallsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task VersionSevenLedger_GainsTheAgentCallsTable()
    {
        using var temp = new TempDirectory();
        await RawSqlite.ExecuteAsync(temp.DatabasePath, SqliteLedger.Schema + SqliteLedger.SchemaVersion2 + SqliteLedger.SchemaVersion3
            + SqliteLedger.SchemaVersion4Sql + SqliteLedger.SchemaVersion5 + SqliteLedger.SchemaVersion6 + "PRAGMA user_version = 7;");

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);

        Assert.Contains("agent_calls", await RawSqlite.StringsAsync(temp.DatabasePath, Tables));
        Assert.Empty(await ledger.GetAgentCallsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnreleasedVersionEightLedgerWithoutAgentCalls_GainsIt_AndKeepsItsCodeMapTables()
    {
        using var temp = new TempDirectory();
        await RawSqlite.ExecuteAsync(temp.DatabasePath, SqliteLedger.Schema + SqliteLedger.SchemaVersion2 + SqliteLedger.SchemaVersion3
            + SqliteLedger.SchemaVersion4Sql + SqliteLedger.SchemaVersion5 + SqliteLedger.SchemaVersion6 + SqliteLedger.SchemaVersion8 + "PRAGMA user_version = 8;");

        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var call = new AgentCall("2026-09-27T10:00:00.0000000Z", "run x", "file:a.cs", UnitKind.File, new AgentIdentity("fake"), AgentUsage.Unknown, true, null, null);
        await ledger.RecordAgentCallAsync(call, CancellationToken.None);

        Assert.Equal(8L, await ledger.ReadPragmaAsync("user_version", CancellationToken.None));
        Assert.Equal([call], await ledger.GetAgentCallsAsync(CancellationToken.None));
        Assert.Contains("code_symbols", await RawSqlite.StringsAsync(temp.DatabasePath, Tables));
    }
}
