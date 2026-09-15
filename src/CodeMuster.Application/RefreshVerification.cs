using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Rebuilds verification packs against current content without discarding the original findings.</summary>
public sealed class RefreshVerification(ILedger ledger, ISourceTree tree, Config? config = null, IContentHasher? hasher = null)
{
    /// <summary>Reopens checks whose code changed, including checks retired by scan. Completed unchanged checks remain done unless the caller forces them.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var files = (await tree.ListFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var units = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var findings = await ledger.GetCurrentFindingsAsync(cancellationToken);
        var deterministic = findings.Where(f => units.GetValueOrDefault(f.UnitId)?.Kind == UnitKind.DeadCode || DeadCodeReview.IsFinding(f.Finding))
            .Select(f => UnitIds.Verify(f.Id)).ToHashSet(StringComparer.Ordinal);
        var current = findings.Where(f => f.Finding.Category != DependencyFindings.Category && !deterministic.Contains(UnitIds.Verify(f.Id))).ToArray();
        var members = await ledger.GetMembersAsync(current.Select(f => f.UnitId).Distinct().ToArray(), cancellationToken);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var checkedUi = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<Unit>();
        var updatedMembers = new List<UnitMember>();
        foreach (var finding in current)
        {
            var id = UnitIds.Verify(finding.Id);
            var parts = new List<UnitMember>();
            if (units[finding.UnitId].Kind == UnitKind.Ux)
            {
                if (config is null || hasher is null)
                    throw new JsonException("browser verification needs current configuration and source hashing; run scan and verify through the CLI");
                if (checkedUi.Add(finding.Finding.Path))
                    await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, finding.Finding.Path, cancellationToken);
                parts.AddRange(members.Where(member => member.UnitId == finding.UnitId).Select(member => member with { UnitId = id }));
            }
            else
            {
                foreach (var path in members.Where(m => m.UnitId == finding.UnitId).Select(m => m.Path).Append(finding.Finding.Path).Distinct().Order(StringComparer.Ordinal))
                {
                    if (!files.TryGetValue(path, out var file)) continue;
                    if (!hashes.TryGetValue(path, out var hash)) hashes[path] = hash = file.KnownHash ?? Hashing.Sha256Hex(await tree.ReadFileAsync(path, cancellationToken));
                    parts.Add(new UnitMember(id, path, null, hash, 0));
                }
            }
            var fingerprint = Fingerprints.Compute(parts);
            units.TryGetValue(id, out var previous);
            var status = previous is null || previous.Status == UnitStatus.Retired || previous.Fingerprint != fingerprint ? UnitStatus.Pending : previous.Status;
            planned.Add(new Unit(id, UnitKind.Verify, FindingLocation.Of(finding.Finding), fingerprint, status, units[finding.UnitId].Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
            updatedMembers.AddRange(parts);
        }
        var retired = units.Values.Where(unit => deterministic.Contains(unit.Id) && unit.Status != UnitStatus.Retired)
            .Select(unit => unit with { Status = UnitStatus.Retired }).ToList();
        planned.AddRange(retired);
        updatedMembers.AddRange(await ledger.GetMembersAsync(retired.Select(unit => unit.Id).ToList(), cancellationToken));
        await ledger.UpsertUnitsAsync(planned, updatedMembers, cancellationToken);
    }
}
