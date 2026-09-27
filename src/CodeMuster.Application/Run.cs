using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Drives one headless agent over every unit that needs work (D10): hands out packs through <see cref="Next"/>, records each response through <see cref="Done"/>, retries failed attempts, and stops cleanly on cancellation. Adapter calls run in a rolling pool (D66): a slot takes the next unit as soon as its call ends. Only the adapter calls run concurrently; the ledger holds one connection, so every ledger call is made by the loop that starts and finishes units. <paramref name="notes"/> hears about each attempt as it starts, since the first result of a long run can be minutes away.</summary>
public sealed class Run(ILedger ledger, ISourceTree tree, IClock clock, Config config, IAgentAdapter adapter, IProgress<RunProgress> progress, IProgress<string>? notes = null)
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
        var total = 0;
        var next = new Next(ledger, tree, config, interactive: false, options.Kind, options.Path);
        var done = new Done(ledger, clock, config, adapter.Identity);
        var calls = new CallRecorder(ledger, clock, config, adapter.Identity, options.Kind == UnitKind.Verify ? "verify" : "run");
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        // In start order, so when several calls have ended the oldest is recorded first and none waits behind newer ones.
        var running = new List<(Task<Attempt> Call, UnitPack Pack)>();
        var gaveUp = new List<string>();
        var skipped = new List<string>();
        var completed = 0;
        using var calling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
                if (free > 0)
                {
                    var busy = running.Select(entry => entry.Pack.UnitId).ToHashSet(StringComparer.Ordinal);
                    var needing = await ledger.NextAsync(int.MaxValue, options.Kind, options.Path, cancellationToken);
                    total = completed + skipped.Count + needing.Count;
                    foreach (var unit in needing.Where(unit => !gaveUp.Contains(unit.Id) && !busy.Contains(unit.Id)).Take(free).ToList())
                    {
                        started = true;
                        await StartAsync(unit);
                    }
                }

                if (running.Count == 0)
                {
                    // A unit that was skipped or rejected without a call may have freed its place for another, so look again before stopping.
                    if (started) continue;
                    return new RunResult(completed, gaveUp, false) { Skipped = skipped };
                }

                var finished = await Task.WhenAny(running.Select(entry => (Task)entry.Call).Append(Volatile.Read(ref wake).Task)).WaitAsync(cancellationToken);
                var index = running.FindIndex(entry => entry.Call == finished);
                if (index < 0)
                {
                    continue;
                }

                var (call, pack) = running[index];
                running.RemoveAt(index);
                var attempt = await call;
                cancellationToken.ThrowIfCancellationRequested();
                var result = attempt.Failure ?? await done.RunAsync(pack.UnitId, pack.Fingerprint, ResponseText.ExtractJson(attempt.Text), cancellationToken);
                if (attempt.Paid is { } paid) await calls.RecordAsync(pack.UnitId, pack.Kind, paid, result.Outcome == DoneOutcome.Recorded, cancellationToken);
                Record(pack.UnitId, pack.Kind, pack.Key, result);
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
        }

        async Task StartAsync(Unit unit)
        {
            UnitPack pack;
            try
            {
                pack = await next.ForUnitAsync(unit.Id, cancellationToken);
            }
            catch (PackTooLargeException ex)
            {
                await ledger.SkipUnitAsync(unit.Id, unit.Fingerprint, ex.Message, cancellationToken);
                skipped.Add(unit.Id);
                progress.Report(new RunProgress(unit.Id, unit.Kind, unit.Key, 0, DoneOutcome.Skipped,
                    "skipped: " + ex.Message, completed, total));
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Record(unit.Id, unit.Kind, unit.Key, new DoneResult(DoneOutcome.Rejected, ex.Message));
                return;
            }

            if (pack.RequiresBrowser)
            {
                gaveUp.Add(pack.UnitId);
                progress.Report(new RunProgress(pack.UnitId, pack.Kind, pack.Key, 0, DoneOutcome.Rejected,
                    "browser evidence required: use codemuster next --kind ux (or verify) in a browser-capable agent session, then done; source review alone cannot complete this unit", completed, total));
                return;
            }

            notes?.Report($"starting {Name(pack.Kind)} {pack.Key}");
            running.Add((CallAsync(pack.Markdown, calling.Token), pack));
        }

        void Record(string unitId, UnitKind kind, string key, DoneResult result)
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

            progress.Report(new RunProgress(unitId, kind, key, attempt, result.Outcome, message, completed, total));
        }
    }

    // Never throws, so the loop can wait on every call at once; a failed call becomes a rejected attempt.
    private async Task<Attempt> CallAsync(string markdown, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await adapter.RunAsync(markdown, cancellationToken);
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
