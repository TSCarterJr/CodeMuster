using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Discovers the tree, refreshes file rows through the stat cache (D05), keeps one File unit per included file, and records a <see cref="ScanRun"/>.</summary>
public sealed class Scan(ILedger ledger, ISourceTree tree, IContentHasher hasher, IClock clock, Config config)
{
    /// <summary>Runs one scan in file mode.</summary>
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

        var existingUnits = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var units = new List<Unit>();
        var members = new List<UnitMember>();
        var created = 0;
        foreach (var record in current.Where(f => f.ExcludedReason is null))
        {
            var member = new UnitMember(UnitIds.File(record.Path), record.Path, null, record.ContentHash, 0);
            var fingerprint = Fingerprints.Compute([member]);
            var lensHash = Config.HashOf(config.LensesFor([(record.Path, record.Language)]));
            existingUnits.TryGetValue(member.UnitId, out var previous);
            if (previous is null || previous.Status == UnitStatus.Retired)
            {
                created++;
            }

            var status = StatusFor(previous, fingerprint, lensHash);
            units.Add(new Unit(member.UnitId, UnitKind.File, record.Path, fingerprint, status, Fidelity.Full, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
            members.Add(member);
        }

        var included = units.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        var retired = existingUnits.Values
            .Where(u => u.Kind == UnitKind.File && u.Status != UnitStatus.Retired && !included.Contains(u.Key))
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
        await ledger.RecordRunAsync(new ScanRun(now, head, included.Count, excluded, total, null), cancellationToken);
        return new ScanResult(head, included.Count, excluded, created, units.Count(u => u.Status == UnitStatus.Stale), total);
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
