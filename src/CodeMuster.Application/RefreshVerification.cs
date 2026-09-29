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
        var checks = new List<(UnitFinding Finding, Unit Source, List<UnitMember> Parts)>();
        foreach (var finding in current)
        {
            var source = units[finding.UnitId];
            var sourceMembers = members[finding.UnitId].ToList();
            var parts = new List<UnitMember>();
            if (source.Kind == UnitKind.Ux)
            {
                if (config is null)
                    throw new JsonException("browser verification needs current configuration and source hashing; run scan and verify through the CLI");
                if (checkedUi.Add(finding.Finding.Path))
                    await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, finding.Finding.Path, cancellationToken);
                parts.AddRange(sourceMembers);
            }
            else
            {
                var paths = SourcePaths(finding, members).Order(StringComparer.Ordinal).ToList();
                var unchanged = source.Status != UnitStatus.Retired
                    && source.Fingerprint == finding.Fingerprint
                    && paths.All(path => hashes.TryGetValue(path, out var hash) && recorded.GetValueOrDefault(path)?.ContentHash == hash);
                parts.AddRange(unchanged
                    ? PlannedUnit.Verify(finding, sourceMembers, source.Fidelity).Members
                    : paths.Where(hashes.ContainsKey).Select(path => new UnitMember("", path, null, hashes[path], 0)));
            }
            checks.Add((finding, source, parts));
        }

        // Findings of one reporting unit checked over the same code share checks (D78): an existing check that stays done keeps its findings, and the rest are batched.
        var batch = config?.VerifyBatch ?? 1;
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in checks.GroupBy(c => c.Finding.UnitId + "\n" + Fingerprints.Compute(c.Parts), StringComparer.Ordinal))
        {
            var byId = group.ToDictionary(c => c.Finding.Id);
            var assigned = new HashSet<long>();
            var existing = units.Values
                .Select(unit => (Unit: unit, Ids: UnitIds.VerifiedFindings(unit.Id)))
                .Where(x => x.Unit.Kind == UnitKind.Verify && x.Ids.Count > 0 && x.Ids.All(byId.ContainsKey))
                .OrderBy(x => x.Unit.Status == UnitStatus.Retired).ThenByDescending(x => x.Ids.Count).ThenBy(x => x.Unit.Id, StringComparer.Ordinal);
            foreach (var (unit, ids) in existing)
            {
                if (ids.Any(assigned.Contains) || Plan(ids) != UnitStatus.Done) continue;
                Add(ids);
                assigned.UnionWith(ids.Where(id => byId[id].Finding.Verification is not null));
            }

            var rest = group.Where(c => !assigned.Contains(c.Finding.Id)).OrderBy(c => c.Finding.Id).ToList();
            foreach (var single in rest.Where(c => ReviewEligibility.RequiresBrowser(c.Finding.Finding, c.Source)))
                Add([single.Finding.Id]);
            foreach (var chunk in rest.Where(c => !ReviewEligibility.RequiresBrowser(c.Finding.Finding, c.Source)).Chunk(Math.Max(1, batch)))
                Add([.. chunk.Select(c => c.Finding.Id)]);

            UnitStatus Plan(IReadOnlyList<long> ids) => Status(UnitIds.Verify(ids), Fingerprints.Compute(byId[ids[0]].Parts), ids.All(id => byId[id].Finding.Verification is not null));

            void Add(IReadOnlyList<long> ids)
            {
                var id = UnitIds.Verify(ids);
                if (!produced.Add(id)) return;
                var first = byId[ids[0]];
                var fingerprint = Fingerprints.Compute(first.Parts);
                units.TryGetValue(id, out var previous);
                var key = ids.Count == 1 ? FindingLocation.Of(first.Finding.Finding)
                    : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{FindingLocation.Of(first.Finding.Finding)} and {ids.Count - 1} more");
                planned.Add(new Unit(id, UnitKind.Verify, key, fingerprint, Plan(ids), first.Source.Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash));
                updatedMembers.AddRange(first.Parts.Select(member => member with { UnitId = id }));
            }
        }

        // A check keeps the status it had while its code is unchanged; a retired check rebuilt exactly as its last verdict saw it is done again.
        UnitStatus Status(string id, string fingerprint, bool verified)
        {
            units.TryGetValue(id, out var previous);
            return previous is null || previous.Fingerprint != fingerprint ? UnitStatus.Pending
                : previous.Status != UnitStatus.Retired ? previous.Status
                : verified && previous.SummaryHash == fingerprint ? UnitStatus.Done
                : UnitStatus.Pending;
        }

        var refreshed = current.Select(f => f.Id).ToHashSet();
        var retired = units.Values.Where(unit => unit.Status != UnitStatus.Retired && !produced.Contains(unit.Id)
                && (deterministic.Contains(unit.Id) || unit.Kind == UnitKind.Verify && UnitIds.VerifiedFindings(unit.Id).Any(refreshed.Contains)))
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
