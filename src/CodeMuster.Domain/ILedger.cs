namespace CodeMuster.Domain;

/// <summary>The SQLite ledger behind every verb (D04). Implementations must be safe for two writers with a busy timeout.</summary>
public interface ILedger
{
    /// <summary>Every file row, including excluded and deleted ones.</summary>
    Task<IReadOnlyList<FileRecord>> GetFilesAsync(CancellationToken cancellationToken);

    /// <summary>Inserts or replaces file rows by path.</summary>
    Task UpsertFilesAsync(IReadOnlyList<FileRecord> files, CancellationToken cancellationToken);

    /// <summary>Every unit row, including retired ones.</summary>
    Task<IReadOnlyList<Unit>> GetUnitsAsync(CancellationToken cancellationToken);

    /// <summary>One unit by id, or null.</summary>
    Task<Unit?> GetUnitAsync(string unitId, CancellationToken cancellationToken);

    /// <summary>Members of the given units.</summary>
    Task<IReadOnlyList<UnitMember>> GetMembersAsync(IReadOnlyList<string> unitIds, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces unit rows by id and replaces the members of every unit in the list.</summary>
    Task UpsertUnitsAsync(IReadOnlyList<Unit> units, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="batch"/> units that need work (pending, stale, or failed), oldest first by insertion order.</summary>
    Task<IReadOnlyList<Unit>> NextAsync(int batch, CancellationToken cancellationToken);

    /// <summary>Stores an analysis and its findings atomically and moves the unit to Done (with summary, summary hash, and lens hash) or Failed.</summary>
    Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken);

    /// <summary>The findings of the most recent successful analysis of every non-retired unit, each with the fingerprint that analysis was made against, so a stale unit's findings still show and can be flagged.</summary>
    Task<IReadOnlyList<UnitFinding>> GetCurrentFindingsAsync(CancellationToken cancellationToken);

    /// <summary>Appends a run.</summary>
    Task RecordRunAsync(Run run, CancellationToken cancellationToken);

    /// <summary>The most recent run, or null before the first scan.</summary>
    Task<Run?> GetLastRunAsync(CancellationToken cancellationToken);
}
