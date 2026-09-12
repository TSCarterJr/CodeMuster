using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeLedger : ILedger
{
    public Dictionary<string, FileRecord> Files { get; } = [];
    public List<Unit> Units { get; } = [];
    public List<UnitMember> Members { get; } = [];
    public List<(Analysis Analysis, IReadOnlyList<Finding> Findings)> Analyses { get; } = [];
    public List<ScanRun> Runs { get; } = [];
    public Dictionary<long, VerifyResponse> Verifications { get; } = [];
    public Dictionary<long, FixOutcome> Fixes { get; } = [];
    public Action<Analysis>? OnRecordAnalysis { get; set; }

    public Task<IReadOnlyList<FileRecord>> GetFilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FileRecord>>(Files.Values.ToList());

    public Task UpsertFilesAsync(IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            Files[file.Path] = file;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Unit>> GetUnitsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Unit>>(Units.ToList());

    public Task<Unit?> GetUnitAsync(string unitId, CancellationToken cancellationToken) =>
        Task.FromResult(Units.FirstOrDefault(u => u.Id == unitId));

    public Task<IReadOnlyList<UnitMember>> GetMembersAsync(IReadOnlyList<string> unitIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnitMember>>(Members.Where(m => unitIds.Contains(m.UnitId)).ToList());

    public Task UpsertUnitsAsync(IReadOnlyList<Unit> units, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        foreach (var unit in units)
        {
            var index = Units.FindIndex(u => u.Id == unit.Id);
            if (index < 0)
            {
                Units.Add(unit);
            }
            else
            {
                Units[index] = unit;
            }

            Members.RemoveAll(m => m.UnitId == unit.Id);
        }

        Members.AddRange(members);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Unit>> NextAsync(int batch, UnitKind? kind, string? path, CancellationToken cancellationToken)
    {
        var folder = path is null ? null : RepoPath.Normalize(path).TrimEnd('/');
        bool Under(Unit unit) => folder is null || Members.Any(m =>
            m.UnitId == unit.Id && (m.Path == folder || m.Path.StartsWith(folder + "/", StringComparison.Ordinal)));
        return Task.FromResult<IReadOnlyList<Unit>>(Units
            .Where(u => (u.Status is UnitStatus.Pending or UnitStatus.Stale or UnitStatus.Failed) && (kind is null || u.Kind == kind) && Under(u))
            .Take(batch)
            .ToList());
    }

    public Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        OnRecordAnalysis?.Invoke(analysis);
        Analyses.Add((analysis, findings));
        var index = Units.FindIndex(u => u.Id == analysis.UnitId);
        var unit = Units[index];
        Units[index] = analysis.Succeeded
            ? unit with { Status = UnitStatus.Done, Summary = analysis.Summary, SummaryHash = analysis.Fingerprint, LensHash = analysis.LensHash }
            : unit with { Status = UnitStatus.Failed };
        return Task.CompletedTask;
    }

    public Task RecordFixAsync(Analysis analysis, IReadOnlyList<(long FindingId, FixOutcome Outcome)> outcomes, CancellationToken cancellationToken)
    {
        foreach (var (findingId, outcome) in outcomes)
        {
            Fixes[findingId] = outcome;
        }

        return RecordAnalysisAsync(analysis, [], cancellationToken);
    }

    public Task RecordVerificationAsync(Analysis analysis, long findingId, VerifyResponse verification, CancellationToken cancellationToken)
    {
        Verifications[findingId] = verification;
        return RecordAnalysisAsync(analysis, [], cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, AgentIdentity>> GetProvenanceAsync(CancellationToken cancellationToken)
    {
        var provenance = Analyses
            .Where(a => a.Analysis.Succeeded && a.Analysis.By is not null)
            .GroupBy(a => a.Analysis.UnitId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Analysis.By!, StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyDictionary<string, AgentIdentity>>(provenance);
    }

    public Task<IReadOnlyList<UnitFinding>> GetCurrentFindingsAsync(CancellationToken cancellationToken)
    {
        var live = Units.Where(u => u.Status != UnitStatus.Retired).Select(u => u.Id).ToHashSet();
        var id = 0L;
        var numbered = Analyses.Select(a => (a.Analysis, Findings: a.Findings.Select(f => (Id: ++id, Finding: f)).ToList())).ToList();
        var current = numbered
            .Where(a => a.Analysis.Succeeded && live.Contains(a.Analysis.UnitId))
            .GroupBy(a => a.Analysis.UnitId)
            .Select(g => g.Last())
            .SelectMany(a => a.Findings.Select(f => new UnitFinding(f.Id, a.Analysis.UnitId, a.Analysis.Fingerprint, f.Finding, Verifications.GetValueOrDefault(f.Id), Fixes.GetValueOrDefault(f.Id))))
            .OrderBy(f => f.Id)
            .ToList();
        return Task.FromResult<IReadOnlyList<UnitFinding>>(current);
    }

    public Task RecordRunAsync(ScanRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<ScanRun?> GetLastRunAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Runs.LastOrDefault());
}
