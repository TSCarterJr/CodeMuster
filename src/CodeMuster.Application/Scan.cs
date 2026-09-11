using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// Discovers the tree, refreshes file rows through the stat cache (D05), plans units, retires every live unit the plan no longer holds, and records a <see cref="ScanRun"/>.
/// A current finding keeps its verify unit while the code of the unit that reported it is unchanged since that analysis (D27).
/// With at least one mapper the scan runs in slice mode: the mappers map the repository at <paramref name="repoRoot"/> and units are slices, orphans, and file units (D25). Without mappers every included file is one file unit.
/// </summary>
public sealed class Scan(ILedger ledger, ISourceTree tree, IContentHasher hasher, IClock clock, Config config, IReadOnlyList<ICodeMapper>? mappers = null, string repoRoot = "")
{
    /// <summary>Runs one scan, in slice mode when mappers were given.</summary>
    public async Task<ScanResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = Timestamps.Format(clock.UtcNow);
        var head = await tree.HeadCommitAsync(cancellationToken);
        var files = await tree.ListFilesAsync(cancellationToken);
        var existing = (await ledger.GetFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);

        var current = new List<FileRecord>(files.Count);
        foreach (var file in files)
        {
            existing.TryGetValue(file.Path, out var previous);
            current.Add(await RefreshAsync(file, previous, now, cancellationToken));
        }

        var present = current.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var deleted = existing.Values
            .Where(f => f.DeletedAt is null && !present.Contains(f.Path))
            .Select(f => f with { DeletedAt = now })
            .ToList();
        await ledger.UpsertFilesAsync([.. current, .. deleted], cancellationToken);

        var included = current.Where(f => f.ExcludedReason is null).ToList();
        IReadOnlyList<ICodeMapper> active = mappers ?? [];
        var mapped = await CompositeMapper.MapAsync(active, repoRoot, included, cancellationToken);
        var planned = SliceBuilder.Build(mapped, included);
        planned = [.. planned, .. await PlanVerifyUnitsAsync(planned, cancellationToken)];

        var existingUnits = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var units = new List<Unit>(planned.Count);
        var created = 0;
        foreach (var plan in planned)
        {
            var fingerprint = Fingerprints.Compute(plan.Members);
            var lensHash = Config.HashOf(config.LensesFor(plan.Members.Select(m => (m.Path, Languages.FromPath(m.Path)))));
            existingUnits.TryGetValue(plan.Id, out var previous);
            if (previous is null || previous.Status == UnitStatus.Retired)
            {
                created++;
            }

            var status = StatusFor(previous, fingerprint, lensHash);
            units.Add(new Unit(plan.Id, plan.Kind, plan.Key, fingerprint, status, plan.Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
        }

        var members = planned.SelectMany(p => p.Members).ToList();
        var produced = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var retired = existingUnits.Values
            .Where(u => u.Status != UnitStatus.Retired && !produced.Contains(u.Id))
            .Select(u => u with { Status = UnitStatus.Retired })
            .ToList();
        if (retired.Count > 0)
        {
            members.AddRange(await ledger.GetMembersAsync(retired.Select(u => u.Id).ToList(), cancellationToken));
        }

        await ledger.UpsertUnitsAsync([.. units, .. retired], members, cancellationToken);

        foreach (var unit in units.Concat(retired))
        {
            existingUnits[unit.Id] = unit;
        }

        var total = existingUnits.Values.Count(u => u.Status != UnitStatus.Retired);
        var excluded = current.Count - included.Count;
        await ledger.RecordRunAsync(new ScanRun(now, head, included.Count, excluded, total, mapped.ResolutionRate, mapped.TopUnresolvedNames), cancellationToken);
        var sliceMode = active.Count == 0
            ? null
            : new SliceModeResult(
                units.Count(u => u.Kind == UnitKind.Slice),
                units.Count(u => u.Kind == UnitKind.Orphan),
                units.Count(u => u.Kind == UnitKind.File),
                mapped.ResolutionRate,
                mapped.Map.Diagnostics);
        return new ScanResult(head, included.Count, excluded, created, units.Count(u => u.Status == UnitStatus.Stale), total, sliceMode);
    }

    private async Task<IEnumerable<PlannedUnit>> PlanVerifyUnitsAsync(IReadOnlyList<PlannedUnit> planned, CancellationToken cancellationToken)
    {
        var sources = planned.ToDictionary(p => p.Id, StringComparer.Ordinal);
        return (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => sources.TryGetValue(f.UnitId, out var source) && Fingerprints.Compute(source.Members) == f.Fingerprint)
            .Select(f => PlannedUnit.Verify(f, sources[f.UnitId].Members, sources[f.UnitId].Fidelity));
    }

    private async Task<FileRecord> RefreshAsync(SourceFile file, FileRecord? previous, string now, CancellationToken cancellationToken)
    {
        var hash = file.KnownHash
            ?? (CanReuseHash(file, previous) ? previous!.ContentHash : await hasher.HashFileAsync(file.Path, cancellationToken));
        var unchanged = previous?.ContentHash == hash;
        return new FileRecord(
            file.Path,
            Languages.FromPath(file.Path),
            hash,
            file.Size,
            file.Mtime,
            previous?.FirstSeen ?? now,
            now,
            file.LastCommit,
            file.LastCommitAt,
            Exclusions.Reason(file.Path, file.LinguistGenerated),
            null,
            unchanged ? previous!.Summary : null,
            unchanged ? previous!.SummaryHash : null);
    }

    private static bool CanReuseHash(SourceFile file, FileRecord? previous) =>
        previous is not null
        && previous.Mtime == file.Mtime
        && previous.Size == file.Size
        && !SameSecond(previous.LastSeen, previous.Mtime);

    private static bool SameSecond(string a, string b) => string.CompareOrdinal(a, 0, b, 0, 19) == 0;

    private static UnitStatus StatusFor(Unit? previous, string fingerprint, string lensHash)
    {
        if (previous is null || previous.Status == UnitStatus.Retired)
        {
            return UnitStatus.Pending;
        }

        if (previous.Status == UnitStatus.Done && (previous.Fingerprint != fingerprint || previous.LensHash != lensHash))
        {
            return UnitStatus.Stale;
        }

        return previous.Status;
    }
}
