using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Fixes what the audit confirmed: one unit per file with confirmed findings, one fresh agent call each, and one commit per changed file. Parallel workers edit in isolation (D40).</summary>
public sealed class Fix(ILedger ledger, ISourceTree? tree = null, IClock? clock = null, Config? config = null, IWorkspace? workspace = null, ITestRunner? tests = null, IFileFixer? fileFixer = null, IContentHasher? hasher = null)
{
    /// <summary>Plans and fixes confirmed findings. With consent, saves tracked local changes and restores them afterward, including on failure or cancellation. With a test runner, first requires the unmodified tree to pass it, before any agent call, unless the options allow failing tests.</summary>
    public async Task<FixResult> RunAsync(IAgentAdapter adapter, FixOptions options, IProgress<string>? notes, CancellationToken cancellationToken)
    {
        if (options.RelatedFiles.Count > 0 && (options.Parallelism != 1 || string.IsNullOrWhiteSpace(options.Path)))
            throw new ArgumentException("related-file recovery requires one --path file and -j 1");
        var eligible = (await EligibleFindingsAsync(cancellationToken)).Where(f => f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State != FixState.Fixed
            && (options.Path is null || f.Finding.Path == options.Path || f.Finding.Path.StartsWith(options.Path.TrimEnd('/') + "/", StringComparison.Ordinal))).ToList();
        if (eligible.Count == 0)
        {
            await PlanAsync(cancellationToken);
            return new FixResult(0, 0, 0, []);
        }
        await CheckBrowserSourcesAsync(eligible, cancellationToken);
        var repository = workspace ?? throw new InvalidOperationException("fix needs a workspace");
        string? stash = null;
        if (!await repository.IsCleanAsync(cancellationToken))
        {
            if (!options.Stash)
            {
                throw new InvalidOperationException("the working tree has uncommitted changes; run codemuster fix in a terminal to be offered a stash, or pass --stash to save and restore them automatically");
            }

            stash = await repository.StashAsync(cancellationToken);
        }

        try
        {
            if (stash is not null)
            {
                notes?.Report($"saved your tracked changes in stash {stash}; they will be restored after fixing");
            }

            return await RunCleanAsync(adapter, options, notes, repository, cancellationToken);
        }
        finally
        {
            if (stash is not null)
            {
                await repository.RestoreStashAsync(stash, CancellationToken.None);
                notes?.Report($"restored your tracked changes and staging; recovery stash {stash} retained");
            }
        }
    }

    private async Task<FixResult> RunCleanAsync(IAgentAdapter adapter, FixOptions options, IProgress<string>? notes, IWorkspace repository, CancellationToken cancellationToken)
    {
        if (!await repository.IsCleanAsync(cancellationToken))
        {
            throw new InvalidOperationException("the working tree still has tracked changes after stashing; no fixes were started");
        }

        await PlanAsync(cancellationToken);
        if (options.RetryDeclined)
        {
            var declinedPaths = (await EligibleFindingsAsync(cancellationToken))
                .Where(f => f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State == FixState.Declined)
                .Select(f => f.Finding.Path).ToHashSet(StringComparer.Ordinal);
            var reopen = (await ledger.GetUnitsAsync(cancellationToken))
                .Where(u => u.Kind == UnitKind.Fix && u.Status == UnitStatus.Done && declinedPaths.Contains(u.Key)
                    && (options.Path is null || u.Key == options.Path || u.Key.StartsWith(options.Path.TrimEnd('/') + "/", StringComparison.Ordinal)))
                .Select(u => u with { Status = UnitStatus.Pending }).ToArray();
            await ledger.UpsertUnitsAsync(reopen, await ledger.GetMembersAsync(reopen.Select(u => u.Id).ToArray(), cancellationToken), cancellationToken);
        }
        return await RunParallelAsync(adapter, options, notes, repository, cancellationToken);
    }

    private async Task<FixResult> RunParallelAsync(IAgentAdapter adapter, FixOptions options, IProgress<string>? notes, IWorkspace repository, CancellationToken cancellationToken)
    {
        var eligible = (await EligibleFindingsAsync(cancellationToken))
            .Where(f => f.Verification?.Verdict == Verdict.Confirmed && f.Fix?.State != FixState.Fixed).ToList();
        var targets = eligible
            .GroupBy(f => f.Finding.Path)
            .ToDictionary(group => group.Key, group => group.Select(f => f.Id).ToHashSet(), StringComparer.Ordinal);
        var selected = (await ledger.NextAsync(int.MaxValue, UnitKind.Fix, options.Path, cancellationToken))
            .Where(unit => targets.ContainsKey(unit.Key)).ToList();
        var fingerprints = selected.ToDictionary(u => u.Id, u => u.Fingerprint, StringComparer.Ordinal);
        var pending = new Queue<string>(selected.Select(u => u.Id));
        if (pending.Count == 0) return new FixResult(0, 0, 0, []);
        await CheckBrowserSourcesAsync(eligible.Where(f => options.Path is null || f.Finding.Path == options.Path || f.Finding.Path.StartsWith(options.Path.TrimEnd('/') + "/", StringComparison.Ordinal)), cancellationToken);
        var editor = fileFixer ?? throw new InvalidOperationException("parallel fix needs isolated file workers");
        if (tests is not null && !options.AllowFailingTests) await CheckBaselineAsync(tests, repository, notes, cancellationToken);
        var next = new Next(ledger, tree!, config ?? Config.Default, interactive: false, UnitKind.Fix, options.Path);
        var done = new Done(ledger, clock!, config ?? Config.Default, adapter.Identity);
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var running = new Dictionary<Task<ParallelAttempt>, UnitPack>();
        var gaveUp = new List<string>();
        var skipped = new List<string>();
        var draining = false;
        var units = 0;
        var fixedCount = 0;
        var declined = 0;
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (pending.Count > 0 || running.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (pending.Count > 0 && running.Count < options.Parallelism)
                {
                    var unitId = pending.Dequeue();
                    UnitPack pack;
                    try
                    {
                        pack = await next.ForUnitAsync(unitId, cancellationToken);
                    }
                    catch (PackTooLargeException ex)
                    {
                        await ledger.SkipUnitAsync(unitId, fingerprints[unitId], ex.Message, cancellationToken);
                        skipped.Add(unitId);
                        notes?.Report("skipped: " + ex.Message);
                        continue;
                    }

                    attempts[pack.UnitId] = attempts.GetValueOrDefault(pack.UnitId) + 1;
                    notes?.Report(string.Create(CultureInfo.InvariantCulture, $"fixing {pack.Key}, {targets[pack.Key].Count} finding(s) (attempt {attempts[pack.UnitId]}/{options.MaxAttempts})"));
                    running.Add(EditAsync(pack), pack);
                }

                if (running.Count == 0) continue;
                var completed = await Task.WhenAny(running.Keys).WaitAsync(cancellationToken);
                var unit = running[completed];
                running.Remove(completed);
                var attempt = await completed;
                FixResponse? response = null;
                if (attempt.Edit is { } edit)
                {
                    if (draining)
                    {
                        notes?.Report($"{unit.Key} finished after the run stopped; its repair was not applied and its worker is retained at {edit.Worktree}");
                        continue;
                    }

                    try
                    {
                        response = await IntegrateAsync(unit, edit);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        draining = true;
                        pending.Clear();
                        gaveUp.Add(unit.UnitId);
                        notes?.Report(string.Create(CultureInfo.InvariantCulture, $"stopped starting repairs after {unit.Key} could not be integrated; the {running.Count} running worker(s) will finish and their repairs are retained unapplied"));
                        continue;
                    }
                    finally
                    {
                        await editor.ReleaseAsync(edit, CancellationToken.None);
                    }
                }

                if (attempt.Error is { } workerError)
                {
                    await RecordFailureAsync(unit, workerError, adapter.Identity, cancellationToken);
                }

                if (response is null)
                {
                    if (attempt.Error is not null)
                    {
                        notes?.Report($"fix failed for {unit.Key}: {attempt.Error}");
                    }

                    if (draining)
                    {
                        gaveUp.Add(unit.UnitId);
                        notes?.Report($"gave up on {unit.Key}; the run stopped before it could be retried");
                    }
                    else if (attempts[unit.UnitId] >= options.MaxAttempts)
                    {
                        gaveUp.Add(unit.UnitId);
                        notes?.Report(string.Create(CultureInfo.InvariantCulture, $"gave up on {unit.Key} after {options.MaxAttempts} attempts; findings remain unfixed"));
                    }
                    else
                    {
                        pending.Enqueue(unit.UnitId);
                        notes?.Report($"queued retry for {unit.Key}");
                    }
                }
                else
                {
                    units++;
                    fixedCount += response.Addressed.Count;
                    declined += response.Declined.Count;
                    notes?.Report($"finished {unit.Key}");
                }
            }

            return new FixResult(units, fixedCount, declined, gaveUp, tests is not null) { Skipped = skipped };
        }
        finally
        {
            await workers.CancelAsync();
            await Task.WhenAll(running.Keys.Select(async task =>
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException) when (workers.IsCancellationRequested)
                {
                }
            }));
        }

        async Task<ParallelAttempt> EditAsync(UnitPack pack)
        {
            try
            {
                return new ParallelAttempt(await editor.RunAsync(pack.Key, pack.Markdown, workers.Token), null);
            }
            catch (Exception ex) when (!workers.IsCancellationRequested)
            {
                return new ParallelAttempt(null, ex.Message);
            }
        }

        async Task<FixResponse?> RejectAsync(UnitPack pack, string error)
        {
            await RecordFailureAsync(pack, error, adapter.Identity, cancellationToken);
            notes?.Report(error);
            return null;
        }

        async Task<FixResponse?> IntegrateAsync(UnitPack pack, FileFixEdit edit)
        {
            var json = ResponseText.ExtractJson(edit.Response);
            FixResponse response;
            try
            {
                response = FixResponseJson.Parse(json);
            }
            catch (JsonException ex)
            {
                return await RejectAsync(pack, $"invalid fix response for {pack.Key}: {ex.Message}");
            }

            var cited = response.Addressed.Concat(response.Declined.Select(d => d.Finding)).ToList();
            var current = (await EligibleFindingsAsync(cancellationToken))
                .Where(finding => finding.Finding.Path == pack.Key && finding.Verification?.Verdict == Verdict.Confirmed && finding.Fix?.State != FixState.Fixed)
                .ToList();
            var currentTargets = current.Select(finding => finding.Id).ToHashSet();
            if (!targets[pack.Key].SetEquals(currentTargets))
                return await RejectAsync(pack, $"repair eligibility changed for {pack.Key}; rescan and refresh browser evidence before retrying; no patch was applied");
            try
            {
                await CheckBrowserSourcesAsync(current, cancellationToken);
            }
            catch (JsonException ex)
            {
                return await RejectAsync(pack, $"browser repair source check failed for {pack.Key}: {ex.Message}; no patch was applied");
            }
            if (cited.Count == 0 || !targets[pack.Key].SetEquals(cited) || cited.Count != targets[pack.Key].Count || string.IsNullOrWhiteSpace(response.Summary) || response.Declined.Any(d => string.IsNullOrWhiteSpace(d.Reason)))
            {
                return await RejectAsync(pack, $"invalid fix response for {pack.Key}: answer every unresolved confirmed finding in this file exactly once, with reasons");
            }

            if (response.Addressed.Count > 0 && edit.Patch.Length == 0)
            {
                return await RejectAsync(pack, $"invalid fix response for {pack.Key}: addressed findings but the file is unchanged; edit it or decline each with a reason");
            }

            var applied = false;
            var recorded = false;
            var preserve = false;
            var allowed = new[] { pack.Key }.Concat(options.RelatedFiles).Distinct(StringComparer.Ordinal).ToArray();
            try
            {
                if (!await repository.IsCleanAsync(cancellationToken))
                    throw new InvalidOperationException("working tree changed during fix; edits preserved; inspect git status before retrying");
                await repository.ApplyPatchAsync(edit.Patch, cancellationToken);
                applied = true;
                if (tests is not null)
                {
                    notes?.Report($"running tests for {pack.Key}");
                    var result = await tests.RunAsync(cancellationToken);
                    var extra = (await repository.ChangedPathsAsync(cancellationToken)).Except(allowed, StringComparer.Ordinal).ToArray();
                    if (extra.Length > 0)
                    {
                        preserve = true;
                        throw new InvalidOperationException("files outside the allowed scope changed while validation ran, written either by the test command or by something else using this checkout: " + string.Join(", ", extra));
                    }
                    if (!result.Passed)
                    {
                        notes?.Report($"tests failed after fixing {pack.Key}, restoring only this file: {LastLine(result.Output)}");
                        return await RejectAsync(pack, "test command failed:\n" + result.Output);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Finish the commit and its ledger record together before observing cancellation again.
                var changed = new List<string>();
                foreach (var path in allowed)
                    if (await repository.HasFileChangesAsync(path, CancellationToken.None)) changed.Add(path);
                if (response.Addressed.Count > 0 && changed.Count == 0)
                {
                    // test_command rewrote the file to its committed content, as a codegen or restore step can; the finally restores the index stat.
                    return await RejectAsync(pack, $"invalid fix response for {pack.Key}: addressed findings but no change remained after validation; edit it or decline each with a reason");
                }

                preserve = true;
                if (changed.Count > 0)
                {
                    if (options.RelatedFiles.Count == 0) await repository.CommitFileAsync(pack.Key, CommitMessage(pack.Key, response), CancellationToken.None);
                    else await repository.CommitFilesAsync(changed, CommitMessage(pack.Key, response), CancellationToken.None);
                }

                var resultDone = await done.RunAsync(pack.UnitId, pack.Fingerprint, json, CancellationToken.None);
                if (resultDone.Outcome != DoneOutcome.Recorded)
                {
                    throw new InvalidOperationException($"could not record fix for {pack.Key}: {resultDone.Message}");
                }

                recorded = true;
                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                preserve = true;
                var error = $"integration failed for {pack.Key}; edits and any completed commit are preserved; inspect git status and reverify before retrying: {ex.Message}";
                notes?.Report(error);
                await RecordFailureAsync(pack, error, adapter.Identity, CancellationToken.None);
                throw new InvalidOperationException(error, ex);
            }
            finally
            {
                if (applied && !recorded && !preserve)
                {
                    foreach (var path in new[] { pack.Key }.Concat(options.RelatedFiles).Distinct(StringComparer.Ordinal))
                        await repository.RestoreFileAsync(path, CancellationToken.None);
                }
            }
        }
    }

    private sealed record ParallelAttempt(FileFixEdit? Edit, string? Error);

    private const string BaselineHint = "Fix the suite or test_command in .codemuster/config.json (codemuster validate runs it), or pass --allow-failing-tests to skip this check.";

    // A suite that cannot pass before any repair rejects every repair, so each attempt would be a paid agent call thrown away.
    private static async Task CheckBaselineAsync(ITestRunner tests, IWorkspace repository, IProgress<string>? notes, CancellationToken cancellationToken)
    {
        notes?.Report("checking test_command on the unmodified tree before any repair");
        TestRun baseline;
        try
        {
            baseline = await tests.RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"test_command could not run on the unmodified tree, so no agent was called: {ex.Message}\n{BaselineHint}", ex);
        }

        if (!baseline.Passed)
        {
            throw new InvalidOperationException($"test_command fails on the unmodified tree, so every repair would be rejected; no agent was called. Its last lines:\n{LastLines(baseline.Output, 20)}\n{BaselineHint}");
        }

        if (!await repository.IsCleanAsync(cancellationToken))
        {
            var changed = string.Join(", ", await repository.ChangedPathsAsync(cancellationToken));
            throw new InvalidOperationException($"test_command changed tracked files on the unmodified tree ({changed}), so it cannot check a repair; no agent was called. Inspect git status and make the command leave tracked files alone, or pass --allow-failing-tests to skip this check.");
        }
    }

    private static string LastLine(string output) => LastLines(output, 1).Trim();

    private static string LastLines(string output, int count)
    {
        var lines = output.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "no output" : string.Join('\n', lines.TakeLast(count));
    }

    private static string CommitMessage(string path, FixResponse response)
    {
        var lines = new List<string> { $"fix {path}", "", response.Summary };
        if (response.Addressed.Count > 0)
        {
            lines.Add("");
            lines.Add("Findings: " + string.Join(", ", response.Addressed.Select(id => id.ToString(CultureInfo.InvariantCulture))));
        }

        if (response.Declined.Count > 0)
        {
            lines.Add("Declined: " + string.Join(", ", response.Declined.Select(d => d.Finding.ToString(CultureInfo.InvariantCulture))));
        }

        return string.Join('\n', lines);
    }

    private Task RecordFailureAsync(UnitPack pack, string error, AgentIdentity identity, CancellationToken cancellationToken)
    {
        var lenses = (config ?? Config.Default).LensesFor([(pack.Key, Languages.FromPath(pack.Key))]);
        return ledger.RecordAnalysisAsync(new Analysis(pack.UnitId, pack.Fingerprint, Config.HashOf(lenses),
            Timestamps.Format(clock!.UtcNow), false, null, error, identity), [], cancellationToken);
    }

    /// <summary>Creates or refreshes a fix unit for every file with a confirmed finding, and retires fix units whose findings are gone. A unit already fixed against the file's current content keeps its Done status, unless a confirmed finding it never answered has since been recorded for that file.</summary>
    public async Task<FixPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var confirmed = (await EligibleFindingsAsync(cancellationToken))
            .Where(f => f.Verification?.Verdict == Verdict.Confirmed)
            .ToList();
        var unresolved = confirmed.Where(f => f.Fix?.State != FixState.Fixed).ToList();
        var unresolvedPaths = unresolved.Select(f => f.Finding.Path).ToHashSet(StringComparer.Ordinal);
        var unansweredPaths = unresolved.Where(f => f.Fix is null).Select(f => f.Finding.Path).ToHashSet(StringComparer.Ordinal);
        var files = (await ledger.GetFilesAsync(cancellationToken))
            .Where(f => f.DeletedAt is null)
            .ToDictionary(f => f.Path, StringComparer.Ordinal);
        var existing = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Kind == UnitKind.Fix)
            .ToDictionary(u => u.Id, StringComparer.Ordinal);

        var planned = confirmed
            .GroupBy(f => f.Finding.Path, StringComparer.Ordinal)
            .Where(group => files.ContainsKey(group.Key))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => PlannedUnit.Fix(group.Key, files[group.Key].ContentHash))
            .ToList();

        var units = planned.Select(plan =>
        {
            var fingerprint = Fingerprints.Compute(plan.Members);
            existing.TryGetValue(plan.Id, out var previous);
            var skipped = previous?.Status == UnitStatus.Skipped;
            var status = !unresolvedPaths.Contains(plan.Key) ? UnitStatus.Done
                : previous is null || skipped || previous.Status == UnitStatus.Retired ? UnitStatus.Pending
                : previous.Status == UnitStatus.Done && (previous.Fingerprint != fingerprint || unansweredPaths.Contains(plan.Key)) ? UnitStatus.Stale
                : previous.Status;
            return new Unit(plan.Id, plan.Kind, plan.Key, fingerprint, status, plan.Fidelity, previous?.LensHash,
                skipped ? null : previous?.Summary, skipped ? null : previous?.SummaryHash);
        }).ToList();

        var produced = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var retired = existing.Values
            .Where(u => u.Status != UnitStatus.Retired && !produced.Contains(u.Id))
            .Select(u => u with { Status = UnitStatus.Retired })
            .ToList();
        var members = planned.SelectMany(p => p.Members).ToList();
        if (retired.Count > 0)
        {
            members.AddRange(await ledger.GetMembersAsync(retired.Select(u => u.Id).ToList(), cancellationToken));
        }

        await ledger.UpsertUnitsAsync([.. units, .. retired], members, cancellationToken);
        return new FixPlan(units.Count, confirmed.Count(f => files.ContainsKey(f.Finding.Path)), units.Count(u => u.Status != UnitStatus.Done));
    }

    private async Task<IReadOnlyList<UnitFinding>> EligibleFindingsAsync(CancellationToken cancellationToken)
    {
        var sources = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        return (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(finding => ReviewEligibility.CanAutoFix(finding, sources.GetValueOrDefault(finding.UnitId), config ?? Config.Default))
            .ToList();
    }

    private async Task CheckBrowserSourcesAsync(IEnumerable<UnitFinding> findings, CancellationToken cancellationToken)
    {
        var sources = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        foreach (var path in findings.Where(finding => ReviewEligibility.RequiresBrowser(finding.Finding, sources.GetValueOrDefault(finding.UnitId)))
            .Select(finding => finding.Finding.Path).Distinct(StringComparer.Ordinal))
        {
            if (tree is null || hasher is null) throw new InvalidOperationException("browser-derived fixes require a source tree and content hasher to check current source; no fixes were started");
            await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config ?? Config.Default, path, cancellationToken);
        }
    }
}
