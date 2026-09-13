using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// Discovers the tree, refreshes file rows through the stat cache (D05), plans units, retires every live unit the plan no longer holds, and records a <see cref="ScanRun"/>.
/// A current finding keeps its verify unit while the code of the unit that reported it is unchanged since that analysis (D27), unless verification is off (D28).
/// With at least one mapper the scan runs in slice mode: the mappers map the repository at <paramref name="repoRoot"/> and units are slices, orphans, and file units (D25). Without mappers every included file is one file unit.
/// Each step is reported to <paramref name="progress"/> as it starts or finishes, with mapper steps under their language.
/// </summary>
public sealed class Scan(ILedger ledger, ISourceTree tree, IContentHasher hasher, IClock clock, Config config, IReadOnlyList<ICodeMapper>? mappers = null, string repoRoot = "", IProgress<string>? progress = null, IDependencyAuditor? auditor = null)
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
        var excluded = current.Count - included.Count;
        progress?.Report($"listed {current.Count} files, {excluded} excluded");
        IReadOnlyList<ICodeMapper> active = mappers ?? [];
        var mapped = await CompositeMapper.MapAsync(active, repoRoot, included, progress, cancellationToken);
        progress?.Report("planning units");
        var planned = SliceBuilder.Build(mapped, included);
        if (config.Verify)
        {
            planned = [.. planned, .. await PlanVerifyUnitsAsync(planned, cancellationToken)];
        }

        var audit = config.Vulnerabilities && auditor is not null
            ? await auditor.AuditAsync(repoRoot, AuditPaths(current), progress, cancellationToken)
            : null;
        if (audit is not null)
        {
            planned = [.. planned, .. PlanDependencyUnitsAsync(audit, included)];
        }

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
        var live = included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var retired = existingUnits.Values
            .Where(u => u.Status != UnitStatus.Retired && !produced.Contains(u.Id))
            .Where(u => u.Kind != UnitKind.Dependency || !live.Contains(u.Key))
            .Select(u => u with { Status = UnitStatus.Retired })
            .ToList();
        if (retired.Count > 0)
        {
            members.AddRange(await ledger.GetMembersAsync(retired.Select(u => u.Id).ToList(), cancellationToken));
        }

        progress?.Report($"saving {units.Count} units");
        await ledger.UpsertUnitsAsync([.. units, .. retired], members, cancellationToken);
        var vulnerabilities = audit is null ? null : await RecordVulnerabilitiesAsync(audit, units, now, cancellationToken);

        foreach (var unit in units.Concat(retired))
        {
            existingUnits[unit.Id] = unit;
        }

        var total = existingUnits.Values.Count(u => u.Status != UnitStatus.Retired);
        await ledger.RecordRunAsync(new ScanRun(now, head, included.Count, excluded, total, mapped.ResolutionRate, mapped.TopUnresolvedNames), cancellationToken);
        var sliceMode = active.Count == 0
            ? null
            : new SliceModeResult(
                units.Count(u => u.Kind == UnitKind.Slice),
                units.Count(u => u.Kind == UnitKind.Orphan),
                units.Count(u => u.Kind == UnitKind.File),
                mapped.ResolutionRate,
                mapped.Map.Diagnostics);
        return new ScanResult(head, included.Count, excluded, created, units.Count(u => u.Status == UnitStatus.Stale), total, sliceMode, vulnerabilities);
    }

    private async Task<IEnumerable<PlannedUnit>> PlanVerifyUnitsAsync(IReadOnlyList<PlannedUnit> planned, CancellationToken cancellationToken)
    {
        var sources = planned.Where(p => p.Kind != UnitKind.Dependency).ToDictionary(p => p.Id, StringComparer.Ordinal);
        return (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => sources.TryGetValue(f.UnitId, out var source) && Fingerprints.Compute(source.Members) == f.Fingerprint)
            .Select(f => PlannedUnit.Verify(f, sources[f.UnitId].Members, sources[f.UnitId].Fidelity));
    }

    /// <summary>Files the audit may look at: everything not excluded, plus the lockfiles, which are excluded as code but are exactly what the audit tools read (D38).</summary>
    private IReadOnlyList<string> AuditPaths(IReadOnlyList<FileRecord> current) =>
        current
            .Where(f => f.DeletedAt is null)
            .Where(f => f.ExcludedReason is null || (f.ExcludedReason == "lockfile" && !config.ExcludedHere(f.Path)))
            .Select(f => f.Path)
            .ToList();

    private static IEnumerable<PlannedUnit> PlanDependencyUnitsAsync(DependencyAudit audit, IReadOnlyList<FileRecord> included)
    {
        var hashes = included.ToDictionary(f => f.Path, f => f.ContentHash, StringComparer.Ordinal);
        return audit.Manifests
            .Where(manifest => hashes.ContainsKey(manifest.Manifest))
            .Select(manifest => PlannedUnit.Dependency(manifest.Manifest, hashes[manifest.Manifest]));
    }

    private async Task<DependencyResult> RecordVulnerabilitiesAsync(DependencyAudit audit, IReadOnlyList<Unit> units, string now, CancellationToken cancellationToken)
    {
        var byId = units.Where(u => u.Kind == UnitKind.Dependency).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var recorded = 0;
        var severities = new Dictionary<Severity, int>();
        foreach (var manifest in audit.Manifests)
        {
            if (!byId.TryGetValue(UnitIds.Dependency(manifest.Manifest), out var unit))
            {
                continue;
            }

            var findings = manifest.Packages.Select(package => Finding(manifest.Manifest, package)).ToList();
            var summary = string.Create(CultureInfo.InvariantCulture, $"{findings.Count} vulnerable package(s) reported by {manifest.Tool}");
            var analysis = new Analysis(unit.Id, unit.Fingerprint, unit.LensHash ?? "", now, true, summary, null, new AgentIdentity(manifest.Tool));
            await ledger.RecordAnalysisAsync(analysis, findings, cancellationToken, new VerifyResponse(Verdict.Confirmed, $"reported by {manifest.Tool}"));
            recorded += findings.Count;
            foreach (var finding in findings)
            {
                severities[finding.Severity] = severities.GetValueOrDefault(finding.Severity) + 1;
            }
        }

        return new DependencyResult(audit.Manifests.Count, recorded, severities, audit.Diagnostics);
    }

    private static Finding Finding(string manifest, VulnerablePackage package)
    {
        var fix = package.FixedVersion is { Length: > 0 } fixedVersion
            ? $" Fixed in {fixedVersion}."
            : " The advisory names no fixed version; check the package's releases.";
        var reach = package.Direct ? "declared here" : "pulled in by another package";
        return new Finding(
            manifest,
            1,
            1,
            package.Severity,
            DependencyFindings.Category,
            $"{package.Package} {package.VulnerableVersions} has a known {package.Severity.ToString().ToLowerInvariant()} severity vulnerability ({package.AdvisoryId}), {reach}.",
            $"{package.Title} {package.AdvisoryUrl}.{fix}",
            1.0,
            DependencyFindings.Lens);
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
            config.ExcludedReason(file.Path, file.LinguistGenerated),
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
