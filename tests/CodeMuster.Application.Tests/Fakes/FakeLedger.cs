using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeLedger : ILedger
{
    public Dictionary<string, FileRecord> Files { get; } = [];
    public List<Unit> Units { get; } = [];
    public List<UnitMember> Members { get; } = [];
    public List<(Analysis Analysis, IReadOnlyList<Finding> Findings)> Analyses { get; } = [];
    public List<Run> Runs { get; } = [];

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

    public Task<IReadOnlyList<Unit>> NextAsync(int batch, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Unit>>(Units
            .Where(u => u.Status is UnitStatus.Pending or UnitStatus.Stale or UnitStatus.Failed)
            .Take(batch)
            .ToList());

    public Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        Analyses.Add((analysis, findings));
        var index = Units.FindIndex(u => u.Id == analysis.UnitId);
        var unit = Units[index];
        Units[index] = analysis.Succeeded
            ? unit with { Status = UnitStatus.Done, Summary = analysis.Summary, SummaryHash = analysis.Fingerprint, LensHash = analysis.LensHash }
            : unit with { Status = UnitStatus.Failed };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnitFinding>> GetCurrentFindingsAsync(CancellationToken cancellationToken)
    {
        var live = Units.Where(u => u.Status != UnitStatus.Retired).Select(u => u.Id).ToHashSet();
        var current = Analyses
            .Where(a => a.Analysis.Succeeded && live.Contains(a.Analysis.UnitId))
            .GroupBy(a => a.Analysis.UnitId)
            .Select(g => g.Last())
            .SelectMany(a => a.Findings.Select(f => new UnitFinding(a.Analysis.UnitId, a.Analysis.Fingerprint, f)))
            .ToList();
        return Task.FromResult<IReadOnlyList<UnitFinding>>(current);
    }

    public Task RecordRunAsync(Run run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<Run?> GetLastRunAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Runs.LastOrDefault());
}
