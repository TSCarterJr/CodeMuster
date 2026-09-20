using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Drives one headless agent over every unit that needs work (D10): hands out packs through <see cref="Next"/>, records each response through <see cref="Done"/>, retries failed attempts, and stops cleanly on cancellation. Adapter calls run concurrently; ledger calls are serialized because the ledger holds one connection. <paramref name="notes"/> hears about each attempt as it starts, since the first result of a long run can be minutes away.</summary>
public sealed class Run(ILedger ledger, ISourceTree tree, IClock clock, Config config, IAgentAdapter adapter, IProgress<RunProgress> progress, IProgress<string>? notes = null)
{
    /// <summary>Runs until nothing needs work or every remaining unit has used its attempts, reporting after every attempt.</summary>
    public async Task<RunResult> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var total = 0;
        var next = new Next(ledger, tree, config, interactive: false, options.Kind, options.Path);
        var done = new Done(ledger, clock, config, adapter.Identity);
        using var turn = new SemaphoreSlim(1, 1);
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var gaveUp = new List<string>();
        var skipped = new List<string>();
        var completed = 0;

        try
        {
            if (options.Force)
            {
                await RestaleDoneUnitsAsync(options.Kind, options.Path, cancellationToken);
            }

            while (true)
            {
                var units = (await ledger.NextAsync(options.Parallelism + gaveUp.Count, options.Kind, options.Path, cancellationToken))
                    .Where(unit => !gaveUp.Contains(unit.Id))
                    .Take(options.Parallelism)
                    .ToList();
                if (units.Count == 0)
                {
                    return new RunResult(completed, gaveUp, false) { Skipped = skipped };
                }

                total = completed + skipped.Count + (await NeedingWorkAsync(options.Kind, options.Path, cancellationToken)).Count;
                var packs = new List<UnitPack>(units.Count);
                foreach (var unit in units)
                {
                    try
                    {
                        packs.Add(await next.ForUnitAsync(unit.Id, cancellationToken));
                    }
                    catch (PackTooLargeException ex)
                    {
                        await ledger.SkipUnitAsync(unit.Id, unit.Fingerprint, ex.Message, cancellationToken);
                        skipped.Add(unit.Id);
                        progress.Report(new RunProgress(unit.Id, unit.Kind, unit.Key, 0, DoneOutcome.Skipped,
                            "skipped: " + ex.Message, completed, total));
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        Record(unit.Id, unit.Kind, unit.Key, new DoneResult(DoneOutcome.Rejected, ex.Message));
                    }
                }
                await Task.WhenAll(packs.Select(AttemptAsync));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(completed, gaveUp, true) { Skipped = skipped };
        }

        async Task AttemptAsync(UnitPack pack)
        {
            if (pack.RequiresBrowser)
            {
                await turn.WaitAsync(cancellationToken);
                try
                {
                    gaveUp.Add(pack.UnitId);
                    progress.Report(new RunProgress(pack.UnitId, pack.Kind, pack.Key, 0, DoneOutcome.Rejected,
                        "browser evidence required: use codemuster next --kind ux (or verify) in a browser-capable agent session, then done; source review alone cannot complete this unit", completed, total));
                }
                finally { turn.Release(); }
                return;
            }
            DoneResult? failure = null;
            var text = "";
            notes?.Report($"starting {Name(pack.Kind)} {pack.Key}");
            try
            {
                text = await adapter.RunAsync(pack.Markdown, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failure = new DoneResult(DoneOutcome.Rejected, ex.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await turn.WaitAsync(cancellationToken);
            try
            {
                var result = failure ?? await done.RunAsync(pack.UnitId, pack.Fingerprint, ResponseText.ExtractJson(text), cancellationToken);
                Record(pack.UnitId, pack.Kind, pack.Key, result);
            }
            finally
            {
                turn.Release();
            }
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

    private async Task<IReadOnlyList<Unit>> NeedingWorkAsync(UnitKind? kind, string? path, CancellationToken cancellationToken) =>
        await ledger.NextAsync(int.MaxValue, kind, path, cancellationToken);

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
