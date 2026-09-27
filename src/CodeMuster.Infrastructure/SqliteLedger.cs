using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;
using Microsoft.Data.Sqlite;

namespace CodeMuster.Infrastructure;

public sealed class SqliteLedger : ILedger, IDisposable
{
    internal const int SchemaVersion = 8;

    internal const string Schema = """
        CREATE TABLE files (
            path TEXT PRIMARY KEY,
            language TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            size INTEGER NOT NULL,
            mtime TEXT NOT NULL,
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL,
            last_commit TEXT,
            last_commit_at TEXT,
            excluded_reason TEXT,
            deleted_at TEXT,
            summary TEXT,
            summary_hash TEXT);
        CREATE TABLE units (
            id TEXT PRIMARY KEY,
            seq INTEGER NOT NULL,
            kind TEXT NOT NULL,
            key TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            status TEXT NOT NULL,
            fidelity TEXT NOT NULL,
            lens_hash TEXT,
            summary TEXT,
            summary_hash TEXT);
        CREATE INDEX units_seq ON units (seq);
        CREATE TABLE unit_members (
            unit_id TEXT NOT NULL,
            path TEXT NOT NULL,
            symbol TEXT,
            member_hash TEXT NOT NULL,
            distance INTEGER NOT NULL);
        CREATE INDEX unit_members_unit_id ON unit_members (unit_id);
        CREATE TABLE runs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at TEXT NOT NULL,
            head_commit TEXT NOT NULL,
            files_included INTEGER NOT NULL,
            files_excluded INTEGER NOT NULL,
            units_total INTEGER NOT NULL,
            resolution_rate REAL);
        CREATE TABLE analyses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            unit_id TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            lens_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            succeeded INTEGER NOT NULL,
            summary TEXT,
            error TEXT);
        CREATE TABLE findings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            analysis_id INTEGER NOT NULL,
            path TEXT NOT NULL,
            line_start INTEGER NOT NULL,
            line_end INTEGER NOT NULL,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            claim TEXT NOT NULL,
            evidence TEXT NOT NULL,
            confidence REAL NOT NULL,
            lens_id TEXT NOT NULL);
        """;

    internal const string SchemaVersion2 = """
        ALTER TABLE unit_members ADD COLUMN start_line INTEGER;
        ALTER TABLE unit_members ADD COLUMN end_line INTEGER;
        ALTER TABLE unit_members ADD COLUMN signature TEXT;
        ALTER TABLE runs ADD COLUMN top_unresolved_names TEXT;
        """;

    internal const string SchemaVersion3 = """
        ALTER TABLE findings ADD COLUMN verify_status TEXT;
        ALTER TABLE findings ADD COLUMN verify_reason TEXT;
        """;

    internal const string SchemaVersion4Sql = """
        ALTER TABLE analyses ADD COLUMN agent TEXT;
        ALTER TABLE analyses ADD COLUMN model TEXT;
        ALTER TABLE analyses ADD COLUMN effort TEXT;
        """;

    internal const string SchemaVersion5 = """
        ALTER TABLE findings ADD COLUMN fix_status TEXT;
        ALTER TABLE findings ADD COLUMN fix_reason TEXT;
        """;

    internal const string SchemaVersion6 = """
        ALTER TABLE analyses ADD COLUMN evidence_json TEXT;
        """;

    // Schema 7 added no DDL: it only gates the 'skipped' unit status so an older build refuses a ledger that holds it.
    internal const string SchemaVersion8 = """
        CREATE TABLE code_map (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            head_commit TEXT NOT NULL,
            scanned_at TEXT NOT NULL,
            mapped_languages TEXT NOT NULL,
            failed_languages TEXT NOT NULL,
            resolved INTEGER NOT NULL,
            unresolved INTEGER NOT NULL,
            top_unresolved_names TEXT NOT NULL,
            diagnostics TEXT NOT NULL,
            inputs_digest TEXT);
        CREATE TABLE code_symbols (
            id TEXT NOT NULL,
            path TEXT NOT NULL,
            start_line INTEGER NOT NULL,
            end_line INTEGER NOT NULL,
            kind TEXT NOT NULL,
            signature TEXT NOT NULL,
            body_hash TEXT NOT NULL,
            container TEXT NOT NULL,
            normalized_hash TEXT);
        CREATE INDEX code_symbols_id ON code_symbols (id);
        CREATE INDEX code_symbols_path ON code_symbols (path);
        CREATE TABLE code_edges (
            from_id TEXT NOT NULL,
            to_id TEXT NOT NULL,
            kind TEXT NOT NULL);
        CREATE INDEX code_edges_from_id ON code_edges (from_id);
        CREATE INDEX code_edges_to_id ON code_edges (to_id);
        CREATE TABLE code_entry_points (
            symbol_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            display TEXT NOT NULL);
        """;

    // Part of schema 8, which no release has shipped: IF NOT EXISTS also gives the table to a ledger that a development build already moved to 8.
    internal const string AgentCallsSql = """
        CREATE TABLE IF NOT EXISTS agent_calls (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_at TEXT NOT NULL,
            run TEXT NOT NULL,
            unit_id TEXT,
            kind TEXT,
            agent TEXT NOT NULL,
            model TEXT,
            effort TEXT,
            answered_model TEXT,
            input_tokens INTEGER,
            output_tokens INTEGER,
            cache_read_tokens INTEGER,
            cache_write_tokens INTEGER,
            cache_write_1h_tokens INTEGER,
            reported_cost_usd TEXT,
            succeeded INTEGER NOT NULL,
            cost_usd TEXT,
            cost_source TEXT);
        """;

    // Part of schema 8 as well (D69): the UI structure the mappers read, stored with the rest of the map so a pack can render it after the scan.
    internal const string UiElementsSql = """
        CREATE TABLE IF NOT EXISTS code_ui_elements (
            kind TEXT NOT NULL,
            text TEXT NOT NULL,
            path TEXT NOT NULL,
            line INTEGER NOT NULL,
            control TEXT,
            target TEXT,
            section TEXT,
            route TEXT);
        """;

    private const string AgentCallColumns =
        "created_at, run, unit_id, kind, agent, model, effort, answered_model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, cache_write_1h_tokens, reported_cost_usd, succeeded, cost_usd, cost_source";

    private const string FileColumns =
        "path, language, content_hash, size, mtime, first_seen, last_seen, last_commit, last_commit_at, excluded_reason, deleted_at, summary, summary_hash";

    private const string UnitColumns = "id, kind, key, fingerprint, status, fidelity, lens_hash, summary, summary_hash";

    private const string MemberColumns = "unit_id, path, symbol, member_hash, distance, start_line, end_line, signature";

    private const string RunColumns = "started_at, head_commit, files_included, files_excluded, units_total, resolution_rate, top_unresolved_names";

    private const string FindingColumns = "path, line_start, line_end, severity, category, claim, evidence, confidence, lens_id";

    private const int InListChunk = 500;

    private readonly SqliteConnection _connection;

    private SqliteLedger(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteLedger> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var ledger = new SqliteLedger(connection);
            await ledger.ExecuteAsync("PRAGMA busy_timeout = 5000", cancellationToken);
            await ledger.ExecuteAsync("PRAGMA journal_mode = WAL", cancellationToken);
            await ledger.MigrateAsync(cancellationToken);
            return ledger;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    internal async Task<long> ReadPragmaAsync(string name, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("PRAGMA " + name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<FileRecord>> GetFilesAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {FileColumns} FROM files ORDER BY path");
        return await ReadAllAsync(command, ReadFile, cancellationToken);
    }

    public async Task UpsertFilesAsync(IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await using var command = CreateCommand($"""
            INSERT INTO files ({FileColumns})
            VALUES ($path, $language, $content_hash, $size, $mtime, $first_seen, $last_seen, $last_commit, $last_commit_at, $excluded_reason, $deleted_at, $summary, $summary_hash)
            ON CONFLICT(path) DO UPDATE SET
                language = excluded.language, content_hash = excluded.content_hash, size = excluded.size, mtime = excluded.mtime,
                first_seen = excluded.first_seen, last_seen = excluded.last_seen, last_commit = excluded.last_commit,
                last_commit_at = excluded.last_commit_at, excluded_reason = excluded.excluded_reason, deleted_at = excluded.deleted_at,
                summary = excluded.summary, summary_hash = excluded.summary_hash
            """);
        foreach (var file in files)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$path", file.Path);
            command.Parameters.AddWithValue("$language", file.Language);
            command.Parameters.AddWithValue("$content_hash", file.ContentHash);
            command.Parameters.AddWithValue("$size", file.Size);
            command.Parameters.AddWithValue("$mtime", file.Mtime);
            command.Parameters.AddWithValue("$first_seen", file.FirstSeen);
            command.Parameters.AddWithValue("$last_seen", file.LastSeen);
            command.Parameters.AddWithValue("$last_commit", Db(file.LastCommit));
            command.Parameters.AddWithValue("$last_commit_at", Db(file.LastCommitAt));
            command.Parameters.AddWithValue("$excluded_reason", Db(file.ExcludedReason));
            command.Parameters.AddWithValue("$deleted_at", Db(file.DeletedAt));
            command.Parameters.AddWithValue("$summary", Db(file.Summary));
            command.Parameters.AddWithValue("$summary_hash", Db(file.SummaryHash));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> GetUnitsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {UnitColumns} FROM units ORDER BY seq");
        return await ReadAllAsync(command, ReadUnit, cancellationToken);
    }

    public async Task<Unit?> GetUnitAsync(string unitId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {UnitColumns} FROM units WHERE id = $id");
        command.Parameters.AddWithValue("$id", unitId);
        return (await ReadAllAsync(command, ReadUnit, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<UnitMember>> GetMembersAsync(IReadOnlyList<string> unitIds, CancellationToken cancellationToken)
    {
        var members = new List<UnitMember>();
        foreach (var chunk in unitIds.Chunk(InListChunk))
        {
            var names = chunk.Select((_, i) => "$id" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
            await using var command = CreateCommand($"SELECT {MemberColumns} FROM unit_members WHERE unit_id IN ({string.Join(", ", names)}) ORDER BY rowid");
            for (var i = 0; i < chunk.Length; i++)
            {
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }

            members.AddRange(await ReadAllAsync(command, ReadMember, cancellationToken));
        }

        return members;
    }

    public async Task UpsertUnitsAsync(IReadOnlyList<Unit> units, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await using var upsert = CreateCommand($"""
            INSERT INTO units (seq, {UnitColumns})
            VALUES ((SELECT COALESCE(MAX(seq), 0) + 1 FROM units), $id, $kind, $key, $fingerprint, $status, $fidelity, $lens_hash, $summary, $summary_hash)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind, key = excluded.key, fingerprint = excluded.fingerprint, status = excluded.status,
                fidelity = excluded.fidelity, lens_hash = excluded.lens_hash, summary = excluded.summary, summary_hash = excluded.summary_hash
            """);
        await using var clear = CreateCommand("DELETE FROM unit_members WHERE unit_id = $unit_id");
        foreach (var unit in units)
        {
            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$id", unit.Id);
            upsert.Parameters.AddWithValue("$kind", Name(unit.Kind));
            upsert.Parameters.AddWithValue("$key", unit.Key);
            upsert.Parameters.AddWithValue("$fingerprint", unit.Fingerprint);
            upsert.Parameters.AddWithValue("$status", Name(unit.Status));
            upsert.Parameters.AddWithValue("$fidelity", Name(unit.Fidelity));
            upsert.Parameters.AddWithValue("$lens_hash", Db(unit.LensHash));
            upsert.Parameters.AddWithValue("$summary", Db(unit.Summary));
            upsert.Parameters.AddWithValue("$summary_hash", Db(unit.SummaryHash));
            await upsert.ExecuteNonQueryAsync(cancellationToken);

            clear.Parameters.Clear();
            clear.Parameters.AddWithValue("$unit_id", unit.Id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = CreateCommand($"INSERT INTO unit_members ({MemberColumns}) VALUES ($unit_id, $path, $symbol, $member_hash, $distance, $start_line, $end_line, $signature)");
        foreach (var member in members)
        {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$unit_id", member.UnitId);
            insert.Parameters.AddWithValue("$path", member.Path);
            insert.Parameters.AddWithValue("$symbol", Db(member.Symbol));
            insert.Parameters.AddWithValue("$member_hash", member.MemberHash);
            insert.Parameters.AddWithValue("$distance", member.Distance);
            insert.Parameters.AddWithValue("$start_line", Db(member.Range?.StartLine));
            insert.Parameters.AddWithValue("$end_line", Db(member.Range?.EndLine));
            insert.Parameters.AddWithValue("$signature", Db(member.Signature));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> NextAsync(int batch, UnitKind? kind, string? path, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"""
            SELECT {UnitColumns} FROM units
            WHERE status IN ($pending, $stale, $failed)
              AND kind != $dependency
              AND (($kind IS NULL AND kind != $fix) OR kind = $kind)
              AND ($path IS NULL OR EXISTS (
                    SELECT 1 FROM unit_members m
                    WHERE m.unit_id = units.id AND (m.path = $path OR substr(m.path, 1, length($under)) = $under)))
            ORDER BY seq LIMIT $batch
            """);
        var folder = path is null ? null : RepoPath.Normalize(path).TrimEnd('/');
        command.Parameters.AddWithValue("$kind", Db(kind is { } k ? Name(k) : null));
        command.Parameters.AddWithValue("$dependency", Name(UnitKind.Dependency));
        command.Parameters.AddWithValue("$fix", Name(UnitKind.Fix));
        command.Parameters.AddWithValue("$path", Db(folder));
        command.Parameters.AddWithValue("$under", Db(folder is null ? null : folder + "/"));
        command.Parameters.AddWithValue("$pending", Name(UnitStatus.Pending));
        command.Parameters.AddWithValue("$stale", Name(UnitStatus.Stale));
        command.Parameters.AddWithValue("$failed", Name(UnitStatus.Failed));
        command.Parameters.AddWithValue("$batch", batch);
        return await ReadAllAsync(command, ReadUnit, cancellationToken);
    }

    public async Task SkipUnitAsync(string unitId, string fingerprint, string reason, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("UPDATE units SET status = 'skipped', summary = $reason, summary_hash = $fingerprint WHERE id = $id AND fingerprint = $fingerprint");
        command.Parameters.AddWithValue("$id", unitId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$reason", reason);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("unit changed before it could be skipped: " + unitId);
    }

    public async Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken, VerifyResponse? verifiedAs = null)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        var analysisId = await InsertAnalysisAsync(analysis, cancellationToken);

        await using var insertFinding = CreateCommand($"""
            INSERT INTO findings (analysis_id, {FindingColumns}, verify_status, verify_reason)
            VALUES ($analysis_id, $path, $line_start, $line_end, $severity, $category, $claim, $evidence, $confidence, $lens_id, $verify_status, $verify_reason)
            """);
        foreach (var finding in findings)
        {
            insertFinding.Parameters.Clear();
            insertFinding.Parameters.AddWithValue("$verify_status", Db(verifiedAs is null ? null : Name(verifiedAs.Verdict)));
            insertFinding.Parameters.AddWithValue("$verify_reason", Db(verifiedAs?.Reason));
            insertFinding.Parameters.AddWithValue("$analysis_id", analysisId);
            insertFinding.Parameters.AddWithValue("$path", finding.Path);
            insertFinding.Parameters.AddWithValue("$line_start", finding.LineStart);
            insertFinding.Parameters.AddWithValue("$line_end", finding.LineEnd);
            insertFinding.Parameters.AddWithValue("$severity", Name(finding.Severity));
            insertFinding.Parameters.AddWithValue("$category", finding.Category);
            insertFinding.Parameters.AddWithValue("$claim", finding.Claim);
            insertFinding.Parameters.AddWithValue("$evidence", finding.Evidence);
            insertFinding.Parameters.AddWithValue("$confidence", finding.Confidence);
            insertFinding.Parameters.AddWithValue("$lens_id", finding.LensId);
            await insertFinding.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpdateUnitAsync(analysis, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordVerificationAsync(Analysis analysis, long findingId, VerifyResponse verification, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await InsertAnalysisAsync(analysis, cancellationToken);
        if (verification.Verdict == Verdict.Confirmed)
        {
            await using var reopen = CreateCommand("UPDATE units SET status = 'stale' WHERE id = (SELECT 'fix:' || path FROM findings WHERE id = $id AND fix_status = 'fixed') AND status != 'retired'");
            reopen.Parameters.AddWithValue("$id", findingId);
            await reopen.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var verdict = CreateCommand("""
            UPDATE findings SET verify_status = $status, verify_reason = $reason,
                fix_status = CASE WHEN $status = 'resolved' THEN 'fixed' WHEN $status = 'confirmed' AND fix_status = 'fixed' THEN NULL ELSE fix_status END,
                fix_reason = CASE WHEN $status = 'resolved' THEN $reason WHEN $status = 'confirmed' AND fix_status = 'fixed' THEN NULL ELSE fix_reason END
            WHERE id = $id
            """);
        verdict.Parameters.AddWithValue("$id", findingId);
        verdict.Parameters.AddWithValue("$status", Name(verification.Verdict));
        verdict.Parameters.AddWithValue("$reason", verification.Reason);
        await verdict.ExecuteNonQueryAsync(cancellationToken);
        await UpdateUnitAsync(analysis, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<long> InsertAnalysisAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        await using var insert = CreateCommand("""
            INSERT INTO analyses (unit_id, fingerprint, lens_hash, created_at, succeeded, summary, error, agent, model, effort, evidence_json)
            VALUES ($unit_id, $fingerprint, $lens_hash, $created_at, $succeeded, $summary, $error, $agent, $model, $effort, $evidence_json)
            RETURNING id
            """);
        insert.Parameters.AddWithValue("$unit_id", analysis.UnitId);
        insert.Parameters.AddWithValue("$fingerprint", analysis.Fingerprint);
        insert.Parameters.AddWithValue("$lens_hash", analysis.LensHash);
        insert.Parameters.AddWithValue("$created_at", analysis.CreatedAt);
        insert.Parameters.AddWithValue("$succeeded", analysis.Succeeded);
        insert.Parameters.AddWithValue("$summary", Db(analysis.Summary));
        insert.Parameters.AddWithValue("$error", Db(analysis.Error));
        insert.Parameters.AddWithValue("$agent", Db(analysis.By?.Agent));
        insert.Parameters.AddWithValue("$model", Db(analysis.By?.Model));
        insert.Parameters.AddWithValue("$effort", Db(analysis.By?.Effort));
        insert.Parameters.AddWithValue("$evidence_json", Db(analysis.EvidenceJson));
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task UpdateUnitAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        await using var update = CreateCommand(analysis.Succeeded
            ? "UPDATE units SET status = $status, summary = $summary, summary_hash = $summary_hash, lens_hash = $lens_hash WHERE id = $unit_id"
            : "UPDATE units SET status = $status WHERE id = $unit_id");
        update.Parameters.AddWithValue("$unit_id", analysis.UnitId);
        update.Parameters.AddWithValue("$status", Name(analysis.Succeeded ? UnitStatus.Done : UnitStatus.Failed));
        update.Parameters.AddWithValue("$summary", Db(analysis.Summary));
        update.Parameters.AddWithValue("$summary_hash", analysis.Fingerprint);
        update.Parameters.AddWithValue("$lens_hash", analysis.LensHash);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UnitFinding>> GetCurrentFindingsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"""
            SELECT f.id, a.unit_id, a.fingerprint, {FindingColumns}, f.verify_status, f.verify_reason, f.fix_status, f.fix_reason
            FROM findings f
            JOIN analyses a ON a.id = f.analysis_id
            JOIN (SELECT unit_id, MAX(id) AS id FROM analyses WHERE succeeded = 1 GROUP BY unit_id) latest ON latest.id = a.id
            JOIN units u ON u.id = a.unit_id
            WHERE u.status != $retired
            ORDER BY f.id
            """);
        command.Parameters.AddWithValue("$retired", Name(UnitStatus.Retired));
        return await ReadAllAsync(command, ReadUnitFinding, cancellationToken);
    }

    public async Task RecordFixAsync(Analysis analysis, IReadOnlyList<(long FindingId, FixOutcome Outcome)> outcomes, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await InsertAnalysisAsync(analysis, cancellationToken);
        await using var update = CreateCommand("UPDATE findings SET fix_status = $status, fix_reason = $reason WHERE id = $id");
        foreach (var (findingId, outcome) in outcomes)
        {
            update.Parameters.Clear();
            update.Parameters.AddWithValue("$id", findingId);
            update.Parameters.AddWithValue("$status", Name(outcome.State));
            update.Parameters.AddWithValue("$reason", outcome.Reason);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpdateUnitAsync(analysis, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, AgentIdentity>> GetProvenanceAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT a.unit_id, a.agent, a.model, a.effort
            FROM analyses a
            JOIN (SELECT unit_id, MAX(id) AS id FROM analyses WHERE succeeded = 1 GROUP BY unit_id) latest ON latest.id = a.id
            WHERE a.agent IS NOT NULL
            """);
        var rows = await ReadAllAsync(
            command,
            reader => (UnitId: reader.GetString(0), Identity: new AgentIdentity(reader.GetString(1), Text(reader, 2), Text(reader, 3))),
            cancellationToken);
        return rows.ToDictionary(row => row.UnitId, row => row.Identity, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, Analysis>> GetLatestEvidenceAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT a.unit_id, a.fingerprint, a.lens_hash, a.created_at, a.summary, a.error, a.agent, a.model, a.effort, a.evidence_json
            FROM analyses a
            JOIN (SELECT unit_id, MAX(id) AS id FROM analyses WHERE succeeded = 1 GROUP BY unit_id) latest ON latest.id = a.id
            JOIN units u ON u.id = a.unit_id
            WHERE u.status != 'retired' AND a.evidence_json IS NOT NULL
            """);
        var rows = await ReadAllAsync(command, reader => new Analysis(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), true, Text(reader, 4), Text(reader, 5),
            reader.IsDBNull(6) ? null : new AgentIdentity(reader.GetString(6), Text(reader, 7), Text(reader, 8)))
        {
            EvidenceJson = reader.GetString(9)
        }, cancellationToken);
        return rows.ToDictionary(analysis => analysis.UnitId, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<Analysis>> GetFailedAnalysesAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT a.unit_id, a.fingerprint, a.lens_hash, a.created_at, a.error, a.agent, a.model, a.effort, a.evidence_json
            FROM analyses a JOIN units u ON u.id = a.unit_id
            WHERE a.succeeded = 0 AND u.status != 'retired'
            ORDER BY a.id
            """);
        return await ReadAllAsync(command, reader => new Analysis(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), false, null, Text(reader, 4),
            reader.IsDBNull(5) ? null : new AgentIdentity(reader.GetString(5), Text(reader, 6), Text(reader, 7)))
        {
            EvidenceJson = Text(reader, 8)
        }, cancellationToken);
    }

    public async Task RecordAgentCallAsync(AgentCall call, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"""
            INSERT INTO agent_calls ({AgentCallColumns})
            VALUES ($created_at, $run, $unit_id, $kind, $agent, $model, $effort, $answered_model, $input_tokens, $output_tokens, $cache_read_tokens, $cache_write_tokens, $cache_write_1h_tokens, $reported_cost_usd, $succeeded, $cost_usd, $cost_source)
            """);
        command.Parameters.AddWithValue("$created_at", call.CreatedAt);
        command.Parameters.AddWithValue("$run", call.Run);
        command.Parameters.AddWithValue("$unit_id", Db(call.UnitId));
        command.Parameters.AddWithValue("$kind", Db(call.Kind is { } kind ? Name(kind) : null));
        command.Parameters.AddWithValue("$agent", call.By.Agent);
        command.Parameters.AddWithValue("$model", Db(call.By.Model));
        command.Parameters.AddWithValue("$effort", Db(call.By.Effort));
        command.Parameters.AddWithValue("$answered_model", Db(call.Usage.Model));
        command.Parameters.AddWithValue("$input_tokens", Db(call.Usage.InputTokens));
        command.Parameters.AddWithValue("$output_tokens", Db(call.Usage.OutputTokens));
        command.Parameters.AddWithValue("$cache_read_tokens", Db(call.Usage.CacheReadTokens));
        command.Parameters.AddWithValue("$cache_write_tokens", Db(call.Usage.CacheWriteTokens));
        command.Parameters.AddWithValue("$cache_write_1h_tokens", Db(call.Usage.CacheWrite1hTokens));
        command.Parameters.AddWithValue("$reported_cost_usd", Db(Money(call.Usage.ReportedCostUsd)));
        command.Parameters.AddWithValue("$succeeded", call.Succeeded);
        command.Parameters.AddWithValue("$cost_usd", Db(Money(call.CostUsd)));
        command.Parameters.AddWithValue("$cost_source", Db(call.CostSource));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentCall>> GetAgentCallsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {AgentCallColumns} FROM agent_calls ORDER BY id");
        return await ReadAllAsync(command, reader => new AgentCall(
            reader.GetString(0), reader.GetString(1), Text(reader, 2),
            Text(reader, 3) is { } kind ? Enum.Parse<UnitKind>(kind, ignoreCase: true) : null,
            new AgentIdentity(reader.GetString(4), Text(reader, 5), Text(reader, 6)),
            new AgentUsage(Long(reader, 8), Long(reader, 9), Long(reader, 10), Long(reader, 11), Text(reader, 7), Money(Text(reader, 13))) { CacheWrite1hTokens = Long(reader, 12) },
            reader.GetBoolean(14), Money(Text(reader, 15)), Text(reader, 16)), cancellationToken);
    }

    // Money is stored as invariant decimal text: REAL would round, and a recorded cost must read back exactly.
    private static string? Money(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static decimal? Money(string? text) => text is null ? null : decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static long? Long(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public async Task RecordRunAsync(ScanRun run, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"INSERT INTO runs ({RunColumns}) VALUES ($started_at, $head_commit, $files_included, $files_excluded, $units_total, $resolution_rate, $top_unresolved_names)");
        command.Parameters.AddWithValue("$started_at", run.StartedAt);
        command.Parameters.AddWithValue("$head_commit", run.HeadCommit);
        command.Parameters.AddWithValue("$files_included", run.FilesIncluded);
        command.Parameters.AddWithValue("$files_excluded", run.FilesExcluded);
        command.Parameters.AddWithValue("$units_total", run.UnitsTotal);
        command.Parameters.AddWithValue("$resolution_rate", Db(run.ResolutionRate));
        command.Parameters.AddWithValue("$top_unresolved_names", Db(run.TopUnresolvedNames is null ? null : string.Join('\n', run.TopUnresolvedNames)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScanRun?> GetLastRunAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {RunColumns} FROM runs ORDER BY id DESC LIMIT 1");
        return (await ReadAllAsync(command, ReadRun, cancellationToken)).SingleOrDefault();
    }

    public async Task ReplaceCodeMapAsync(StoredCodeMap map, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync("DELETE FROM code_map; DELETE FROM code_symbols; DELETE FROM code_edges; DELETE FROM code_entry_points; DELETE FROM code_ui_elements;", cancellationToken);

        await using (var header = CreateCommand("""
            INSERT INTO code_map (id, head_commit, scanned_at, mapped_languages, failed_languages, resolved, unresolved, top_unresolved_names, diagnostics, inputs_digest)
            VALUES (1, $head_commit, $scanned_at, $mapped_languages, $failed_languages, $resolved, $unresolved, $top_unresolved_names, $diagnostics, $inputs_digest)
            """))
        {
            header.Parameters.AddWithValue("$head_commit", map.HeadCommit);
            header.Parameters.AddWithValue("$scanned_at", map.ScannedAt);
            header.Parameters.AddWithValue("$mapped_languages", JsonList(map.MappedLanguages));
            header.Parameters.AddWithValue("$failed_languages", JsonList(map.FailedLanguages));
            header.Parameters.AddWithValue("$resolved", map.Map.Resolution.Resolved);
            header.Parameters.AddWithValue("$unresolved", map.Map.Resolution.Unresolved);
            header.Parameters.AddWithValue("$top_unresolved_names", JsonList(map.Map.Resolution.TopUnresolvedNames));
            header.Parameters.AddWithValue("$diagnostics", JsonList(map.Map.Diagnostics));
            header.Parameters.AddWithValue("$inputs_digest", Db(map.InputsDigest));
            await header.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertRowsAsync(
            "INSERT INTO code_symbols (id, path, start_line, end_line, kind, signature, body_hash, container, normalized_hash) VALUES ($p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9)",
            map.Map.Symbols,
            symbol => [symbol.Id, symbol.Path, symbol.Range.StartLine, symbol.Range.EndLine, symbol.Kind, symbol.Signature, symbol.BodyHash, SymbolContainer.Of(symbol), (object?)symbol.NormalizedHash ?? DBNull.Value],
            cancellationToken);
        await InsertRowsAsync(
            "INSERT INTO code_edges (from_id, to_id, kind) VALUES ($p1, $p2, $p3)",
            map.Map.Edges,
            edge => [edge.From, edge.To, Name(edge.Kind)],
            cancellationToken);
        await InsertRowsAsync(
            "INSERT INTO code_entry_points (symbol_id, kind, display) VALUES ($p1, $p2, $p3)",
            map.Map.EntryPoints,
            entry => [entry.SymbolId, entry.Kind, entry.Display],
            cancellationToken);
        await InsertRowsAsync(
            "INSERT INTO code_ui_elements (kind, text, path, line, control, target, section, route) VALUES ($p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8)",
            map.Map.UiElements,
            element => [element.Kind, element.Text, element.Path, element.Line, Db(element.Control), Db(element.Target), Db(element.Section), Db(element.Route)],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StoredCodeMap?> GetCodeMapAsync(CancellationToken cancellationToken)
    {
        await using var header = CreateCommand("""
            SELECT head_commit, scanned_at, mapped_languages, failed_languages, resolved, unresolved, top_unresolved_names, diagnostics, inputs_digest FROM code_map
            """);
        var headers = await ReadAllAsync(header, reader => (
            Head: reader.GetString(0), At: reader.GetString(1), Mapped: ReadJsonList(reader, 2), Failed: ReadJsonList(reader, 3),
            Resolution: new ResolutionStats(reader.GetInt32(4), reader.GetInt32(5), ReadJsonList(reader, 6)), Diagnostics: ReadJsonList(reader, 7), Digest: Text(reader, 8)), cancellationToken);
        if (headers.Count == 0)
        {
            return null;
        }

        await using var symbols = CreateCommand("SELECT id, path, start_line, end_line, kind, signature, body_hash, normalized_hash FROM code_symbols ORDER BY rowid");
        await using var edges = CreateCommand("SELECT from_id, to_id, kind FROM code_edges ORDER BY rowid");
        await using var entries = CreateCommand("SELECT symbol_id, kind, display FROM code_entry_points ORDER BY rowid");
        await using var ui = CreateCommand("SELECT kind, text, path, line, control, target, section, route FROM code_ui_elements ORDER BY rowid");
        var rawEdges = await ReadAllAsync(edges, reader => (From: reader.GetString(0), To: reader.GetString(1), Kind: reader.GetString(2)), cancellationToken);
        // A newer build may store an edge kind this one does not know; the rest of the map is still worth reading.
        var unknownKinds = rawEdges.Where(edge => !IsKnownEdgeKind(edge.Kind)).GroupBy(edge => edge.Kind, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} edge(s) of kind '{group.Key}' are unknown to this version of codemuster and were skipped"));
        var map = new CodeMap(
            await ReadAllAsync(symbols, reader => new Symbol(
                reader.GetString(0), reader.GetString(1), new LineRange(reader.GetInt32(2), reader.GetInt32(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)), cancellationToken),
            rawEdges.Where(edge => IsKnownEdgeKind(edge.Kind)).Select(edge => new Edge(edge.From, edge.To, Enum.Parse<EdgeKind>(edge.Kind, ignoreCase: true))).ToList(),
            await ReadAllAsync(entries, reader => new EntryPoint(reader.GetString(0), reader.GetString(1), reader.GetString(2)), cancellationToken),
            headers[0].Resolution,
            [.. headers[0].Diagnostics, .. unknownKinds])
        {
            UiElements = await ReadAllAsync(ui, reader => new UiElement(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7)), cancellationToken),
        };
        return new StoredCodeMap(headers[0].Head, headers[0].At, map, headers[0].Mapped, headers[0].Failed) { InputsDigest = headers[0].Digest };
    }

    private static bool IsKnownEdgeKind(string kind) => Enum.TryParse<EdgeKind>(kind, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !char.IsAsciiDigit(kind[0]);

    /// <summary>Inserts every row through one prepared command whose parameters are created once, because a large repository's map has tens of thousands of rows.</summary>
    private async Task InsertRowsAsync<T>(string sql, IReadOnlyList<T> rows, Func<T, object[]> values, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var insert = CreateCommand(sql);
        var parameters = values(rows[0]).Select((_, i) => insert.Parameters.Add(new SqliteParameter("$p" + (i + 1).ToString(CultureInfo.InvariantCulture), null))).ToArray();
        insert.Prepare();
        foreach (var row in rows)
        {
            var rowValues = values(row);
            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = rowValues[i];
            }

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string JsonList(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);

    private static IReadOnlyList<string> ReadJsonList(SqliteDataReader reader, int ordinal) =>
        JsonSerializer.Deserialize<List<string>>(reader.GetString(ordinal)) ?? [];

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        var version = await ReadPragmaAsync("user_version", cancellationToken);
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"this ledger was written by a newer codemuster (schema {version}, this build reads up to {SchemaVersion}); update codemuster"));
        }

        if (version < 1)
        {
            await ExecuteAsync(Schema, cancellationToken);
        }

        if (version < 2)
        {
            await ExecuteAsync(SchemaVersion2, cancellationToken);
        }

        if (version < 3)
        {
            await ExecuteAsync(SchemaVersion3, cancellationToken);
        }

        if (version < 4)
        {
            await ExecuteAsync(SchemaVersion4Sql, cancellationToken);
        }

        if (version < 5)
        {
            await ExecuteAsync(SchemaVersion5, cancellationToken);
        }

        if (version < 6)
        {
            await ExecuteAsync(SchemaVersion6, cancellationToken);
        }

        if (version < 8)
        {
            await ExecuteAsync(SchemaVersion8, cancellationToken);
        }

        await ExecuteAsync(AgentCallsSql, cancellationToken);
        await ExecuteAsync(UiElementsSql, cancellationToken);
        await AddColumnIfMissingAsync("code_symbols", "normalized_hash", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync("code_map", "inputs_digest", "TEXT", cancellationToken);

        await ExecuteAsync(string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {SchemaVersion}"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Schema 8 has not shipped, so a ledger a development build already moved to 8 may lack a column added since.
    private async Task AddColumnIfMissingAsync(string table, string column, string type, CancellationToken cancellationToken)
    {
        await using var info = CreateCommand($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'");
        if (Convert.ToInt64(await info.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
        {
            await ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}", cancellationToken);
        }
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteCommand CreateCommand(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(SqliteCommand command, Func<SqliteDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static FileRecord ReadFile(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        Text(reader, 7), Text(reader, 8), Text(reader, 9), Text(reader, 10), Text(reader, 11), Text(reader, 12));

    private static Unit ReadUnit(SqliteDataReader reader) => new(
        reader.GetString(0), Enum.Parse<UnitKind>(reader.GetString(1), ignoreCase: true), reader.GetString(2), reader.GetString(3),
        Enum.Parse<UnitStatus>(reader.GetString(4), ignoreCase: true), Enum.Parse<Fidelity>(reader.GetString(5), ignoreCase: true),
        Text(reader, 6), Text(reader, 7), Text(reader, 8));

    private static UnitMember ReadMember(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), Text(reader, 2), reader.GetString(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : new LineRange(reader.GetInt32(5), reader.GetInt32(6)), Text(reader, 7));

    private static UnitFinding ReadUnitFinding(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        new Finding(reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), Enum.Parse<Severity>(reader.GetString(6), ignoreCase: true),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetDouble(10), reader.GetString(11)),
        Text(reader, 12) is { } verdict ? new VerifyResponse(Enum.Parse<Verdict>(verdict, ignoreCase: true), reader.GetString(13)) : null,
        Text(reader, 14) is { } fix ? new FixOutcome(Enum.Parse<FixState>(fix, ignoreCase: true), reader.GetString(15)) : null);

    private static ScanRun ReadRun(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetDouble(5),
        Text(reader, 6) is { } names ? (names.Length == 0 ? [] : names.Split('\n')) : null);

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static object Db(object? value) => value ?? DBNull.Value;
}
