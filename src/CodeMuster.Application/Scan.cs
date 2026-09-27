using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// Discovers the tree, refreshes file rows through the stat cache (D05), plans units, retires every live unit the plan no longer holds, and records a <see cref="ScanRun"/>.
/// A current finding keeps its verify unit while the code of the unit that reported it is unchanged since that analysis (D27), unless verification is off (D28);
/// a check that verify completed over whole files whose content is unchanged stays done in the reporting unit's form.
/// With at least one mapper the scan runs in slice mode: the mappers map the repository at <paramref name="repoRoot"/> and units are slices, orphans, and file units (D25). Without mappers every included file is one file unit.
/// In slice mode the scan also replaces the stored code map with what the mappers returned, including a failed mapper's diagnostic, at the scanned commit (D60); a file-mode scan leaves it as it was.
/// Slices never read the stored map. In slice mode the scan reads it once, before replacing it: to reuse it instead of mapping when <see cref="MapInputs"/> proves nothing the mappers read changed and no remap was asked for (D66), and, with impact review on, to plan an impact unit for each symbol whose body or signature changed (D67).
/// Only the stored map carries the UI-to-API join (D61): its <see cref="EdgeKind.Http"/> edges and diagnostics never reach slices, fingerprints or the result's diagnostics, and one progress line summarizes it when the mappers found an HTTP call.
/// Each step is reported to <paramref name="progress"/> as it starts or finishes, with mapper steps under their language.
/// </summary>
public sealed class Scan(ILedger ledger, ISourceTree tree, IContentHasher hasher, IClock clock, Config config, IReadOnlyList<ICodeMapper>? mappers = null, string repoRoot = "", IProgress<string>? progress = null, IDependencyAuditor? auditor = null, IEngineEvents? events = null, bool remap = false)
{
    // Every step also reaches the engine stream (D65) when there is one.
    private readonly IProgress<string>? progress = EngineStream.Tee(progress, events);

    /// <summary>Runs one scan, in slice mode when mappers were given.</summary>
    public async Task<ScanResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = Timestamps.Format(clock.UtcNow);
        var head = await tree.HeadCommitAsync(cancellationToken);
        var files = await tree.ListFilesAsync(cancellationToken);
        var existing = (await ledger.GetFilesAsync(cancellationToken)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var hashes = await ContentHashes.CurrentAsync(hasher, files, existing, cancellationToken);
        var current = files.Select(file => Refresh(file, existing.GetValueOrDefault(file.Path), hashes[file.Path], now)).ToList();

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
        var mappingInputs = current.Where(f => config.IsMappingInput(f.Path, f.ExcludedReason)).ToList();
        var auditPaths = AuditPaths(current);
        var auditSet = auditPaths.ToHashSet(StringComparer.Ordinal);
        var auditFiles = current.Where(f => auditSet.Contains(f.Path)).ToList();
        // The audit tools and the mappers are separate processes, so the audit runs while mapping does (D66); it is awaited before its units are planned.
        using var auditCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var auditing = config.Vulnerabilities && auditor is not null
            ? auditor.AuditAsync(repoRoot, auditPaths, progress, auditCancellation.Token)
            : null;
        (CompositeMap Mapped, HttpLinkResult Linked, List<PlannedUnit> Planned, DeadCodeScan? DeadCode, string? Digest) mapping;
        try
        {
            mapping = await MapAndPlanAsync(active, mappingInputs, included, current, cancellationToken);
        }
        catch when (auditing is not null)
        {
            // Stop the audit's processes before this failure leaves the scan; it is the one to report.
            await auditCancellation.CancelAsync();
            await auditing.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }

        var (mapped, linked, planned, deadCode, digest) = mapping;
        var audit = auditing is null ? null : Merged(await auditing);
        if (audit is not null)
        {
            planned = [.. planned, .. PlanDependencyUnitsAsync(audit, auditFiles)];
        }

        var existingUnits = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var verified = (await ledger.GetMembersAsync(
            planned.Where(p => p.Kind == UnitKind.Verify && existingUnits.GetValueOrDefault(p.Id)?.Status == UnitStatus.Done).Select(p => p.Id).ToList(),
            cancellationToken)).ToLookup(m => m.UnitId, StringComparer.Ordinal);
        // An impact unit nobody has analyzed yet keeps waiting (D67): a later scan that sees its symbol unchanged does not retire it,
        // and a further change keeps the baseline it was first planned against, so the pack still shows everything since the last review.
        var awaiting = existingUnits.Values.Where(u => u.Kind == UnitKind.Impact && u.Status is UnitStatus.Pending or UnitStatus.Failed or UnitStatus.Skipped)
            .Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var baselines = (await ledger.GetMembersAsync(planned.Where(p => awaiting.Contains(p.Id)).Select(p => p.Id).ToList(), cancellationToken))
            .Where(m => m.Distance < 0).ToDictionary(m => m.UnitId, StringComparer.Ordinal);
        planned = [.. planned.Select(p => baselines.TryGetValue(p.Id, out var baseline)
            ? p with { Members = [.. p.Members.Select(m => m.Distance < 0 ? baseline : m)] }
            : p)];
        var symbols = linked.Map.Symbols.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

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
            if (status == UnitStatus.Stale && previous!.LensHash == lensHash && RefreshVerification.CoveredByWholeFiles([.. verified[plan.Id]], plan.Members, hashes))
            {
                status = UnitStatus.Done;
            }
            units.Add(new Unit(plan.Id, plan.Kind, plan.Key, fingerprint, status, plan.Fidelity, previous?.LensHash, previous?.Status == UnitStatus.Skipped ? null : previous?.Summary, previous?.Status == UnitStatus.Skipped ? null : previous?.SummaryHash));
        }

        var members = planned.SelectMany(p => p.Members).ToList();
        var produced = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var live = auditFiles.ToDictionary(f => f.Path, f => Fingerprints.Compute(PlannedUnit.Dependency(f.Path, f.ContentHash).Members), StringComparer.Ordinal);
        var retired = existingUnits.Values
            .Where(u => u.Status != UnitStatus.Retired && !produced.Contains(u.Id))
            .Where(u => u.Kind != UnitKind.Dependency || !live.TryGetValue(u.Key, out var fingerprint) || u.Fingerprint != fingerprint)
            .Where(u => !awaiting.Contains(u.Id) || !symbols.Contains(u.Id[UnitIds.Impact("").Length..]))
            .Select(u => u with { Status = UnitStatus.Retired })
            .ToList();
        if (retired.Count > 0)
        {
            members.AddRange(await ledger.GetMembersAsync(retired.Select(u => u.Id).ToList(), cancellationToken));
        }

        progress?.Report($"saving {units.Count} units");
        await ledger.UpsertUnitsAsync([.. units, .. retired], members, cancellationToken);
        var vulnerabilities = audit is null ? null : await RecordVulnerabilitiesAsync(audit, units, now, cancellationToken);
        if (deadCode is not null) await deadCode.RecordAsync(ledger, config, now, cancellationToken);

        foreach (var unit in units.Concat(retired))
        {
            existingUnits[unit.Id] = unit;
        }

        if (active.Count > 0)
        {
            await ledger.ReplaceCodeMapAsync(new StoredCodeMap(head, now, linked.Map, mapped.MappedLanguages, mapped.FailedLanguages) { InputsDigest = digest }, cancellationToken);
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
        var result = new ScanResult(head, included.Count, excluded, created, units.Count(u => u.Status == UnitStatus.Stale), total, sliceMode, vulnerabilities);
        events.Emit("scan_summary", ("head", result.HeadCommit), ("files_included", result.FilesIncluded), ("files_excluded", result.FilesExcluded),
            ("units_created", result.UnitsCreated), ("units_stale", result.UnitsStale), ("units_total", result.UnitsTotal),
            ("slices", sliceMode?.Slices), ("orphans", sliceMode?.Orphans), ("resolution_rate", sliceMode?.ResolutionRate), ("vulnerable_packages", vulnerabilities?.Packages));
        return result;
    }

    private async Task<IEnumerable<PlannedUnit>> PlanVerifyUnitsAsync(IReadOnlyList<PlannedUnit> planned, CancellationToken cancellationToken)
    {
        var sources = planned.Where(p => p.Kind is not (UnitKind.Dependency or UnitKind.DeadCode)).ToDictionary(p => p.Id, StringComparer.Ordinal);
        return (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => sources.TryGetValue(f.UnitId, out var source) && Fingerprints.Compute(source.Members) == f.Fingerprint)
            .Select(f => PlannedUnit.Verify(f, sources[f.UnitId].Members, sources[f.UnitId].Fidelity));
    }

    /// <summary>Files the audit may look at: everything not excluded, plus data manifests and lockfiles, which are excluded as code but are exactly what the audit tools read (D38).</summary>
    /// <summary>
    /// Maps the repository, or reuses the stored map when <see cref="MapInputs"/> proves mapping again would return it (D66) and <c>--remap</c> was not given, and plans every unit except the dependency units, which wait for the audit.
    /// Returns the digest to store with the map, null when it may not be reused.
    /// </summary>
    private async Task<(CompositeMap Mapped, HttpLinkResult Linked, List<PlannedUnit> Planned, DeadCodeScan? DeadCode, string? Digest)> MapAndPlanAsync(
        IReadOnlyList<ICodeMapper> active, IReadOnlyList<FileRecord> mappingInputs, IReadOnlyList<FileRecord> included, IReadOnlyList<FileRecord> current, CancellationToken cancellationToken)
    {
        var stored = active.Count > 0 && (config.Impact || !remap) ? await ledger.GetCodeMapAsync(cancellationToken) : null;
        var digest = active.Count > 0 ? MapInputs.Digest(active, mappingInputs, current) : null;
        CompositeMap mapped;
        HttpLinkResult linked;
        if (!remap && stored?.InputsDigest is { } storedDigest && storedDigest == digest)
        {
            progress?.Report($"reused the code map from {stored.HeadCommit[..Math.Min(7, stored.HeadCommit.Length)]}; nothing mapped changed");
            mapped = MapInputs.Unlinked(stored);
            linked = new HttpLinkResult(stored.Map, 0, 0, 0, 0, false);
            progress?.Report("planning units");
        }
        else
        {
            mapped = await CompositeMapper.MapAsync(active, repoRoot, mappingInputs, progress, cancellationToken);
            progress?.Report("planning units");
            linked = HttpLinks.Join(mapped.Map);
            if (linked.Summary is { } summary)
            {
                progress?.Report(summary);
            }
        }

        List<PlannedUnit> planned = [.. SliceBuilder.Build(mapped, included), .. UxReview.Plan(included, config.UserExperience)];
        if (config.Impact && active.Count > 0)
        {
            planned = [.. planned, .. ImpactReview.Plan(stored, linked.Map, included)];
        }

        if (config.Duplicates && active.Count > 0)
        {
            planned = [.. planned, .. DuplicateReview.Plan(mapped.Map, included)];
        }

        if (config.ArchitectureReview && active.Count > 0)
        {
            planned = [.. planned, .. ArchitectureReview.PlanUi(linked.Map, included), .. ArchitectureReview.PlanApi(linked.Map, included)];
        }

        DeadCodeScan? deadCode = null;
        if (config.DeadCode)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in included)
            {
                try { sources[file.Path] = await tree.ReadFileAsync(file.Path, cancellationToken); }
                catch (IOException ex) { progress?.Report($"dead-code source unavailable for {file.Path}: {ex.Message}"); }
            }
            deadCode = DeadCodeScan.Build(mapped, included, sources);
            planned = [.. planned, .. deadCode.Plans];
        }
        if (config.Verify)
        {
            planned = [.. planned, .. await PlanVerifyUnitsAsync(planned, cancellationToken)];
        }

        return (mapped, linked, planned, deadCode, digest is null ? null : MapInputs.Storable(digest, mapped, active, mappingInputs));
    }

    private IReadOnlyList<string> AuditPaths(IReadOnlyList<FileRecord> current) =>
        current
            .Where(f => f.DeletedAt is null)
            .Where(f => f.ExcludedReason is null || (f.ExcludedReason is "lockfile" or "data" && !config.ExcludedHere(f.Path)))
            .Select(f => f.Path)
            .ToList();

    /// <summary>One entry per manifest, so a manifest reported twice cannot plan two units with one id.</summary>
    private static DependencyAudit Merged(DependencyAudit audit) => audit with
    {
        Manifests = audit.Manifests
            .GroupBy(manifest => manifest.Manifest, StringComparer.Ordinal)
            .Select(group => group.Count() == 1
                ? group.First()
                : new ManifestVulnerabilities(group.Key, string.Join(" and ", group.Select(m => m.Tool).Distinct()), group.SelectMany(m => m.Packages).Distinct().ToList()))
            .ToList(),
    };

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
            // The lens hash scan plans for this unit, so the next unchanged scan finds the recorded analysis current instead of stale.
            var lensHash = Config.HashOf(config.LensesFor([(manifest.Manifest, Languages.FromPath(manifest.Manifest))]));
            var analysis = new Analysis(unit.Id, unit.Fingerprint, lensHash, now, true, summary, null, new AgentIdentity(manifest.Tool));
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

    private FileRecord Refresh(SourceFile file, FileRecord? previous, string hash, string now)
    {
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

    private static UnitStatus StatusFor(Unit? previous, string fingerprint, string lensHash)
    {
        if (previous is null || previous.Status is UnitStatus.Retired or UnitStatus.Skipped)
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
