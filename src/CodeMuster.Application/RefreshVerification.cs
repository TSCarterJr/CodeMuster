using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Rebuilds verification packs against current content without discarding the original findings.</summary>
public sealed class RefreshVerification(ILedger ledger, ISourceTree tree)
{
    /// <summary>Reopens checks whose code changed, including checks retired by scan. Completed unchanged checks remain done unless the caller forces them.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var files = (await tree.ListFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var current = (await ledger.GetCurrentFindingsAsync(cancellationToken)).Where(f => f.Finding.Category != DependencyFindings.Category).ToArray();
        var units = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var members = await ledger.GetMembersAsync(current.Select(f => f.UnitId).Distinct().ToArray(), cancellationToken);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var planned = new List<Unit>();
        var updatedMembers = new List<UnitMember>();
        foreach (var finding in current)
        {
            var id = UnitIds.Verify(finding.Id);
            var parts = new List<UnitMember>();
            foreach (var path in members.Where(m => m.UnitId == finding.UnitId).Select(m => m.Path).Append(finding.Finding.Path).Distinct().Order(StringComparer.Ordinal))
            {
                if (!files.TryGetValue(path, out var file)) continue;
                if (!hashes.TryGetValue(path, out var hash)) hashes[path] = hash = file.KnownHash ?? Hashing.Sha256Hex(await tree.ReadFileAsync(path, cancellationToken));
                parts.Add(new UnitMember(id, path, null, hash, 0));
            }
            var fingerprint = Fingerprints.Compute(parts);
            units.TryGetValue(id, out var previous);
            var status = previous is null || previous.Status == UnitStatus.Retired || previous.Fingerprint != fingerprint ? UnitStatus.Pending : previous.Status;
            planned.Add(new Unit(id, UnitKind.Verify, FindingLocation.Of(finding.Finding), fingerprint, status, units[finding.UnitId].Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
            updatedMembers.AddRange(parts);
        }
        await ledger.UpsertUnitsAsync(planned, updatedMembers, cancellationToken);
    }
}
