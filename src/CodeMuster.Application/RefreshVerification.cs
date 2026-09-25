using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Rebuilds verification packs against current content without discarding the original findings.</summary>
public sealed class RefreshVerification(ILedger ledger, ISourceTree tree, Config? config, IContentHasher hasher)
{
    /// <summary>
    /// Reopens checks whose code changed, including checks retired by scan. Completed unchanged checks remain done unless the caller forces them,
    /// and so does a retired check rebuilt exactly as its last verdict saw it.
    /// While the reporting unit and its files are as the last scan recorded them, a check keeps that unit's members, exactly as done and scan build it; otherwise it covers the whole current files, hashed the way scan hashes them.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var files = (await tree.ListFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var units = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var findings = await ledger.GetCurrentFindingsAsync(cancellationToken);
        var deterministic = findings.Where(f => units.GetValueOrDefault(f.UnitId)?.Kind == UnitKind.DeadCode || DeadCodeReview.IsFinding(f.Finding))
            .Select(f => UnitIds.Verify(f.Id)).ToHashSet(StringComparer.Ordinal);
        var current = findings.Where(f => f.Finding.Category != DependencyFindings.Category && !deterministic.Contains(UnitIds.Verify(f.Id))).ToArray();
        var members = (await ledger.GetMembersAsync(current.Select(f => f.UnitId).Distinct().ToArray(), cancellationToken)).ToLookup(m => m.UnitId, StringComparer.Ordinal);
        var recorded = (await ledger.GetFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var cited = current.Where(f => units[f.UnitId].Kind != UnitKind.Ux).SelectMany(f => SourcePaths(f, members)).ToHashSet(StringComparer.Ordinal);
        var hashes = await ContentHashes.CurrentAsync(hasher, files.Values.Where(f => cited.Contains(f.Path)), recorded, cancellationToken);
        var checkedUi = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<Unit>();
        var updatedMembers = new List<UnitMember>();
        foreach (var finding in current)
        {
            var id = UnitIds.Verify(finding.Id);
            var source = units[finding.UnitId];
            var sourceMembers = members[finding.UnitId].ToList();
            var parts = new List<UnitMember>();
            if (source.Kind == UnitKind.Ux)
            {
                if (config is null)
                    throw new JsonException("browser verification needs current configuration and source hashing; run scan and verify through the CLI");
                if (checkedUi.Add(finding.Finding.Path))
                    await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, finding.Finding.Path, cancellationToken);
                parts.AddRange(sourceMembers.Select(member => member with { UnitId = id }));
            }
            else
            {
                var paths = SourcePaths(finding, members).Order(StringComparer.Ordinal).ToList();
                var unchanged = source.Status != UnitStatus.Retired
                    && source.Fingerprint == finding.Fingerprint
                    && paths.All(path => hashes.TryGetValue(path, out var hash) && recorded.GetValueOrDefault(path)?.ContentHash == hash);
                parts.AddRange(unchanged
                    ? PlannedUnit.Verify(finding, sourceMembers, source.Fidelity).Members
                    : paths.Where(hashes.ContainsKey).Select(path => new UnitMember(id, path, null, hashes[path], 0)));
            }
            var fingerprint = Fingerprints.Compute(parts);
            units.TryGetValue(id, out var previous);
            var status = previous is null || previous.Fingerprint != fingerprint ? UnitStatus.Pending
                : previous.Status != UnitStatus.Retired ? previous.Status
                : finding.Verification is not null && previous.SummaryHash == fingerprint ? UnitStatus.Done
                : UnitStatus.Pending;
            planned.Add(new Unit(id, UnitKind.Verify, FindingLocation.Of(finding.Finding), fingerprint, status, source.Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
            updatedMembers.AddRange(parts);
        }
        var retired = units.Values.Where(unit => deterministic.Contains(unit.Id) && unit.Status != UnitStatus.Retired)
            .Select(unit => unit with { Status = UnitStatus.Retired }).ToList();
        planned.AddRange(retired);
        updatedMembers.AddRange(await ledger.GetMembersAsync(retired.Select(unit => unit.Id).ToList(), cancellationToken));
        await ledger.UpsertUnitsAsync(planned, updatedMembers, cancellationToken);
    }

    /// <summary>
    /// True when a check was verified over whole files that still hash to <paramref name="hashes"/> and include every file of <paramref name="planned"/>,
    /// so that verdict already covers the reporting unit's members and scan can keep it done in their form.
    /// </summary>
    internal static bool CoveredByWholeFiles(IReadOnlyList<UnitMember> verified, IReadOnlyList<UnitMember> planned, IReadOnlyDictionary<string, string> hashes) =>
        verified.Count > 0
        && verified.All(member => member.Symbol is null && hashes.GetValueOrDefault(member.Path) == member.MemberHash)
        && planned.All(member => verified.Any(file => file.Path == member.Path));

    private static IEnumerable<string> SourcePaths(UnitFinding finding, ILookup<string, UnitMember> members) =>
        members[finding.UnitId].Select(member => member.Path).Append(finding.Finding.Path).Distinct(StringComparer.Ordinal);
}
