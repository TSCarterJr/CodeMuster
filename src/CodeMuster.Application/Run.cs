using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Drives one headless agent over every unit that needs work (D10): hands out packs through <see cref="Next"/>, records each response through <see cref="Done"/>, retries failed attempts, and stops cleanly on cancellation. Adapter calls run in a rolling pool (D66): a slot takes the next unit as soon as its call ends. Only the adapter calls run concurrently; the ledger holds one connection, so every ledger call is made by the loop that starts and finishes units. <paramref name="notes"/> hears about each attempt as it starts, since the first result of a long run can be minutes away.
/// With <paramref name="control"/>, engine commands pause, resume, stop or resize the run while it works (D65); a model or effort command applies to units started afterwards through an adapter from <paramref name="retarget"/>, and is rejected without one.</summary>
public sealed class Run(ILedger ledger, ISourceTree tree, IClock clock, Config config, IAgentAdapter adapter, IProgress<RunProgress> progress, IProgress<string>? notes = null, IEngineEvents? events = null, IEngineControl? control = null, Func<AgentIdentity, IAgentAdapter>? retarget = null)
{
    private int workers;
    private TaskCompletionSource wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Changes how many adapter calls the running pool keeps in flight. A smaller pool lets calls already running finish; a larger one starts more units at once.</summary>
    public void Resize(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        Volatile.Write(ref this.workers, workers);
        Volatile.Read(ref wake).TrySetResult();
    }

    /// <summary>Runs until nothing needs work or every remaining unit has used its attempts, reporting after every attempt.</summary>
    public async Task<RunResult> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        Volatile.Write(ref workers, options.Parallelism);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A stop command cancels exactly as Ctrl+C does, so from here on the run watches both.
        cancellationToken = stopping.Token;
        var steering = new Steering(control, events, options.Parallelism, adapter.Identity, retarget is not null, Resize, stopping.Cancel);
        var adapters = new Dictionary<AgentIdentity, IAgentAdapter> { [adapter.Identity] = adapter };
        var total = 0;
        var next = new Next(ledger, tree, config, interactive: false, options.Kind, options.Path);
        var command = options.Kind == UnitKind.Verify ? "verify" : "run";
        var calls = new CallRecorder(ledger, clock, config, adapter.Identity, command);
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        // In start order, so when several calls have ended the oldest is recorded first and none waits behind newer ones.
        var running = new List<(Task<Attempt> Call, IReadOnlyList<UnitPack> Packs, AgentIdentity By, int Worker, DateTimeOffset StartedAt)>();
        var gaveUp = new List<string>();
        // Units a batched call failed, or left out of its reply, are retried alone (D80).
        var solo = new HashSet<string>(StringComparer.Ordinal);
        var skipped = new List<string>();
        var completed = 0;
        using var calling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        events.Started(command, options.Parallelism, adapter.Identity);

        try
        {
            if (options.Force)
            {
                await RestaleDoneUnitsAsync(options.Kind, options.Path, cancellationToken);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (wake.Task.IsCompleted) Volatile.Write(ref wake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                var free = Volatile.Read(ref workers) - running.Count;
                var started = false;
                var remaining = false;
                if (free > 0)
                {
                    var busy = running.SelectMany(entry => entry.Packs).Select(pack => pack.UnitId).ToHashSet(StringComparer.Ordinal);
                    var needing = await ledger.NextAsync(int.MaxValue, options.Kind, options.Path, cancellationToken);
                    total = completed + skipped.Count + needing.Count;
                    var startable = needing.Where(unit => !gaveUp.Contains(unit.Id) && !busy.Contains(unit.Id)).ToList();
                    remaining = startable.Count > 0;
                    var claimed = new HashSet<string>(StringComparer.Ordinal);
                    var built = new Dictionary<string, UnitPack>(StringComparer.Ordinal);
                    var slots = steering.Paused ? 0 : free;
                    for (var i = 0; i < startable.Count && slots > 0; i++)
                    {
                        if (!claimed.Add(startable[i].Id)) continue;
                        started = true;
                        if (await BuildAsync(startable[i], built) is not { } pack) continue;
                        Launch([pack, .. await CompanionsAsync(pack, startable.Skip(i + 1).Where(unit => !claimed.Contains(unit.Id)).Take(Lookahead).ToList(), claimed, built)]);
                        slots--;
                    }
                }

                if (running.Count == 0)
                {
                    // A unit that was skipped or rejected without a call may have freed its place for another, so look again before stopping.
                    // A paused run with work left waits for the next command instead.
                    if (started) continue;
                    if (!remaining) return new RunResult(completed, gaveUp, false) { Skipped = skipped };
                }

                var finished = await Task.WhenAny(running.Select(entry => (Task)entry.Call).Append(Volatile.Read(ref wake).Task).Append(steering.Arrival(calling.Token)))
                    .WaitAsync(cancellationToken);
                if (steering.TryApply()) continue;
                var index = running.FindIndex(entry => entry.Call == finished);
                if (index < 0)
                {
                    continue;
                }

                var (call, packs, by, _, startedAt) = running[index];
                running.RemoveAt(index);
                var attempt = await call;
                cancellationToken.ThrowIfCancellationRequested();
                if (packs.Count == 1)
                {
                    var single = attempt.Failure ?? await new Done(ledger, clock, config, by).RunAsync(packs[0].UnitId, packs[0].Fingerprint, ResponseText.ExtractJson(attempt.Text), cancellationToken);
                    await FinishAsync(packs[0], single, attempt.Paid, by, startedAt);
                    continue;
                }

                IReadOnlyDictionary<string, AnalysisResponse>? answers = null;
                string? unreadable = null;
                if (attempt.Failure is null)
                {
                    try { answers = BatchPack.Split(ResponseText.ExtractJson(attempt.Text)); }
                    catch (System.Text.Json.JsonException ex) { unreadable = ex.Message; }
                }

                var shares = Shares(attempt.Paid, packs);
                for (var p = 0; p < packs.Count; p++)
                {
                    var pack = packs[p];
                    var each = attempt.Failure
                        ?? (unreadable is not null ? new DoneResult(DoneOutcome.Rejected, $"the batched response was not valid ({unreadable}); this unit runs alone next")
                        : answers!.TryGetValue(pack.UnitId, out var answer)
                            ? await new Done(ledger, clock, config, by).RunAsync(pack.UnitId, pack.Fingerprint, AnalysisResponseJson.Serialize(answer), cancellationToken)
                            : new DoneResult(DoneOutcome.Rejected, "the batched response left this unit out; it runs alone next"));
                    if (each.Outcome != DoneOutcome.Recorded) solo.Add(pack.UnitId);
                    await FinishAsync(pack, each, shares?[p], by, startedAt);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(completed, gaveUp, true) { Skipped = skipped };
        }
        finally
        {
            await calling.CancelAsync();
            await Task.WhenAll(running.Select(entry => entry.Call));
            events.Emit("run_summary", ("command", command), ("completed", completed), ("gave_up", gaveUp.Count), ("skipped", skipped.Count), ("cancelled", cancellationToken.IsCancellationRequested));
        }

        async Task FinishAsync(UnitPack pack, DoneResult result, AgentUsage? paid, AgentIdentity by, DateTimeOffset startedAt)
        {
            var spent = paid is not null ? await calls.RecordAsync(pack.UnitId, pack.Kind, paid, result.Outcome == DoneOutcome.Recorded, cancellationToken, by) : null;
            var report = Record(pack.UnitId, pack.Kind, pack.Key, result);
            events.UnitFinished(pack.UnitId, pack.Kind, pack.Key, report.Attempt, EngineStream.Name(result.Outcome), report.Message,
                gaveUp.Contains(pack.UnitId), EngineStream.Milliseconds(startedAt, clock.UtcNow), spent);
            foreach (var finding in result.Findings)
            {
                events.Emit("finding_recorded", ("unit", pack.UnitId), ("path", finding.Path), ("line", finding.LineStart), ("severity", finding.Severity.ToString().ToLowerInvariant()), ("category", finding.Category));
            }

            if (result.Verdict is { } verdict)
            {
                events.Emit("verify_outcome", ("unit", pack.UnitId), ("key", pack.Key), ("verdict", verdict.ToString().ToLowerInvariant()));
            }

            foreach (var each in result.Verdicts)
            {
                events.Emit("verify_outcome", ("unit", pack.UnitId), ("key", pack.Key), ("finding", each.Finding), ("verdict", each.Verdict.ToString().ToLowerInvariant()));
            }
        }

        // Builds a unit's pack, or handles it without a call: an oversized pack is skipped, a pack that cannot be built is a rejected attempt, and browser work is given up to a browser-capable session.
        async Task<UnitPack?> BuildAsync(Unit unit, Dictionary<string, UnitPack> built)
        {
            UnitPack pack;
            try
            {
                pack = built.Remove(unit.Id, out var ready) ? ready : await next.ForUnitAsync(unit.Id, cancellationToken);
            }
            catch (PackTooLargeException ex)
            {
                await ledger.SkipUnitAsync(unit.Id, unit.Fingerprint, ex.Message, cancellationToken);
                skipped.Add(unit.Id);
                progress.Report(new RunProgress(unit.Id, unit.Kind, unit.Key, 0, DoneOutcome.Skipped,
                    "skipped: " + ex.Message, completed, total));
                events.Skipped(unit.Id, unit.Kind, unit.Key, ex.Message);
                return null;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var report = Record(unit.Id, unit.Kind, unit.Key, new DoneResult(DoneOutcome.Rejected, ex.Message));
                events.Emit("unit_not_started", ("unit", unit.Id), ("kind", EngineStream.Name(unit.Kind)), ("key", unit.Key), ("attempt", report.Attempt), ("reason", report.Message), ("gave_up", gaveUp.Contains(unit.Id)));
                return null;
            }

            if (pack.RequiresBrowser)
            {
                const string BrowserOnly = "browser evidence required: use codemuster next --kind ux (or verify) in a browser-capable agent session, then done; source review alone cannot complete this unit";
                gaveUp.Add(pack.UnitId);
                progress.Report(new RunProgress(pack.UnitId, pack.Kind, pack.Key, 0, DoneOutcome.Rejected, BrowserOnly, completed, total));
                events.Emit("unit_not_started", ("unit", pack.UnitId), ("kind", EngineStream.Name(pack.Kind)), ("key", pack.Key), ("attempt", 0), ("reason", BrowserOnly), ("gave_up", true));
                return null;
            }

            return pack;
        }

        // A small first-attempt pack takes following units with the same lenses and directories while they fit (D80); the rest wait for their own turn.
        async Task<IReadOnlyList<UnitPack>> CompanionsAsync(UnitPack first, IReadOnlyList<Unit> following, HashSet<string> claimed, Dictionary<string, UnitPack> built)
        {
            if (config.BatchUnits <= 1 || !Batchable(first) || following.Count == 0) return [];
            var members = (await ledger.GetMembersAsync([first.UnitId, .. following.Select(unit => unit.Id)], cancellationToken))
                .ToLookup(member => member.UnitId, StringComparer.Ordinal);
            var key = GroupKey(first, members);
            var group = new List<UnitPack>();
            var tokens = Tokens(first);
            foreach (var unit in following.Where(unit => unit.Kind is not (UnitKind.Verify or UnitKind.Fix)))
            {
                if (group.Count + 1 >= config.BatchUnits) break;
                UnitPack pack;
                try
                {
                    pack = built.TryGetValue(unit.Id, out var ready) ? ready : built[unit.Id] = await next.ForUnitAsync(unit.Id, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                if (!Batchable(pack) || GroupKey(pack, members) != key || tokens + Tokens(pack) > config.SliceTokenBudget) continue;
                tokens += Tokens(pack);
                group.Add(pack);
                claimed.Add(unit.Id);
                built.Remove(unit.Id);
            }

            return group;
        }

        bool Batchable(UnitPack pack) =>
            pack.Kind is not (UnitKind.Verify or UnitKind.Fix) && !pack.RequiresBrowser && !solo.Contains(pack.UnitId)
            && attempts.GetValueOrDefault(pack.UnitId) == 0 && Tokens(pack) <= config.SliceTokenBudget / 4;

        void Launch(IReadOnlyList<UnitPack> packs)
        {
            notes?.Report(packs.Count == 1
                ? $"starting {Name(packs[0].Kind)} {packs[0].Key}"
                : string.Create(CultureInfo.InvariantCulture, $"starting {packs.Count} units together: {string.Join(", ", packs.Select(pack => pack.Key))}"));
            var identity = steering.Identity;
            if (!adapters.TryGetValue(identity, out var agent)) adapters[identity] = agent = retarget!(identity);
            var worker = EngineStream.FreeWorker(running.Select(entry => entry.Worker));
            foreach (var pack in packs)
            {
                events.UnitStarted(pack.UnitId, pack.Kind, pack.Key, worker, attempts.GetValueOrDefault(pack.UnitId) + 1);
            }

            var markdown = packs.Count == 1 ? packs[0].Markdown : BatchPack.Compose(packs);
            running.Add((CallAsync(agent, markdown, calling.Token), packs, identity, worker, clock.UtcNow));
        }

        RunProgress Record(string unitId, UnitKind kind, string key, DoneResult result)
        {
            var attempt = attempts.GetValueOrDefault(unitId) + 1;
            attempts[unitId] = attempt;
            var message = result.Message;
            if (result.Outcome == DoneOutcome.Recorded)
            {
                completed++;
            }
            else if (attempt >= options.MaxAttempts)
            {
                gaveUp.Add(unitId);
                message = string.Create(CultureInfo.InvariantCulture, $"gave up after {attempt} attempt(s): {result.Message}");
            }

            var report = new RunProgress(unitId, kind, key, attempt, result.Outcome, message, completed, total);
            progress.Report(report);
            return report;
        }
    }

    private const int Lookahead = 16;

    private static long Tokens(UnitPack pack) => pack.Markdown.Length / 4;

    // Units batch only with the same lens line and the same member directories (D80).
    private static string GroupKey(UnitPack pack, ILookup<string, UnitMember> members) =>
        pack.Markdown.Split('\n').FirstOrDefault(line => line.StartsWith("- lenses: ", StringComparison.Ordinal)) + "\n"
        + string.Join(",", members[pack.UnitId].Select(member => member.Path.LastIndexOf('/') is var slash and >= 0 ? member.Path[..slash] : "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    // A batch call's usage split across its units in proportion to pack size, the last unit taking what rounding leaves.
    private static IReadOnlyList<AgentUsage>? Shares(AgentUsage? paid, IReadOnlyList<UnitPack> packs)
    {
        if (paid is null) return null;
        var weights = packs.Select(pack => (decimal)Math.Max(1, pack.Markdown.Length)).ToList();
        var total = weights.Sum();
        long? Part(long? value, int i) => value is not { } v ? null
            : i == packs.Count - 1 ? v - Enumerable.Range(0, i).Sum(j => (long)Math.Floor(v * weights[j] / total))
            : (long)Math.Floor(v * weights[i] / total);
        decimal? Money(decimal? value, int i) => value is not { } v ? null
            : i == packs.Count - 1 ? v - Enumerable.Range(0, i).Sum(j => Math.Round(v * weights[j] / total, 6))
            : Math.Round(v * weights[i] / total, 6);
        return [.. packs.Select((_, i) => new AgentUsage(Part(paid.InputTokens, i), Part(paid.OutputTokens, i), Part(paid.CacheReadTokens, i), Part(paid.CacheWriteTokens, i), paid.Model, Money(paid.ReportedCostUsd, i))
        {
            CacheWrite1hTokens = Part(paid.CacheWrite1hTokens, i),
        })];
    }

    // Never throws, so the loop can wait on every call at once; a failed call becomes a rejected attempt.
    private static async Task<Attempt> CallAsync(IAgentAdapter agent, string markdown, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await agent.RunAsync(markdown, cancellationToken);
            return new Attempt(reply.Text, reply.Usage, null);
        }
        catch (Exception ex)
        {
            return new Attempt("", CallRecorder.PaidUsage(ex), new DoneResult(DoneOutcome.Rejected, ex.Message));
        }
    }

    // Paid is the usage to record, or null when the harness never ran; Failure is set when the call itself failed.
    private sealed record Attempt(string Text, AgentUsage? Paid, DoneResult? Failure);

    private async Task<IReadOnlyList<Unit>> UnderPathAsync(IReadOnlyList<Unit> units, string? path, CancellationToken cancellationToken)
    {
        if (path is null || units.Count == 0)
        {
            return units;
        }

        var folder = RepoPath.Normalize(path).TrimEnd('/');
        var members = await ledger.GetMembersAsync(units.Select(u => u.Id).ToList(), cancellationToken);
        var under = members
            .Where(m => m.Path == folder || m.Path.StartsWith(folder + "/", StringComparison.Ordinal))
            .Select(m => m.UnitId)
            .ToHashSet(StringComparer.Ordinal);
        return units.Where(u => under.Contains(u.Id)).ToList();
    }

    private async Task RestaleDoneUnitsAsync(UnitKind? kind, string? path, CancellationToken cancellationToken)
    {
        var done = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Status is UnitStatus.Done or UnitStatus.Skipped && u.Kind is not (UnitKind.Dependency or UnitKind.DeadCode) && (kind is null ? u.Kind != UnitKind.Fix : u.Kind == kind))
            .ToList();
        var stale = (await UnderPathAsync(done, path, cancellationToken))
            .Select(u => u.Status == UnitStatus.Skipped
                ? u with { Status = UnitStatus.Pending, Summary = null, SummaryHash = null }
                : u with { Status = UnitStatus.Stale })
            .ToList();
        if (stale.Count == 0)
        {
            return;
        }

        var members = await ledger.GetMembersAsync(stale.Select(u => u.Id).ToList(), cancellationToken);
        await ledger.UpsertUnitsAsync(stale, members, cancellationToken);
    }

    private static string Name(UnitKind kind) => kind.ToString().ToLowerInvariant();
}
