using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Drives one headless agent over every unit that needs work (D10): hands out packs through <see cref="Next"/>, records each response through <see cref="Done"/>, retries failed attempts, and stops cleanly on cancellation. Adapter calls run concurrently; ledger calls are serialized because the ledger holds one connection.</summary>
public sealed class Run(ILedger ledger, ISourceTree tree, IClock clock, Config config, IAgentAdapter adapter, IProgress<RunProgress> progress)
{
    /// <summary>Runs until nothing needs work or every remaining unit has used its attempts, reporting after every attempt.</summary>
    public async Task<RunResult> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        var total = await CountPendingAsync(options.Force, cancellationToken);
        var next = new Next(ledger, tree, config, interactive: false);
        var done = new Done(ledger, clock, config);
        using var turn = new SemaphoreSlim(1, 1);
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var gaveUp = new List<string>();
        var completed = 0;

        try
        {
            while (true)
            {
                var packs = (await next.RunAsync(options.Parallelism + gaveUp.Count, cancellationToken))
                    .Where(pack => !gaveUp.Contains(pack.UnitId))
                    .Take(options.Parallelism)
                    .ToList();
                if (packs.Count == 0)
                {
                    return new RunResult(completed, gaveUp, false);
                }

                await Task.WhenAll(packs.Select(AttemptAsync));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(completed, gaveUp, true);
        }

        async Task AttemptAsync(UnitPack pack)
        {
            DoneResult? failure = null;
            var text = "";
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
                Record(pack.UnitId, result);
            }
            finally
            {
                turn.Release();
            }
        }

        void Record(string unitId, DoneResult result)
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

            progress.Report(new RunProgress(unitId, attempt, result.Outcome, message, completed, total));
        }
    }

    private async Task<int> CountPendingAsync(bool force, CancellationToken cancellationToken)
    {
        var units = await ledger.GetUnitsAsync(cancellationToken);
        var pending = units.Count(u => u.Status is UnitStatus.Pending or UnitStatus.Stale or UnitStatus.Failed);
        if (!force)
        {
            return pending;
        }

        var stale = units.Where(u => u.Status == UnitStatus.Done).Select(u => u with { Status = UnitStatus.Stale }).ToList();
        if (stale.Count == 0)
        {
            return pending;
        }

        var members = await ledger.GetMembersAsync(stale.Select(u => u.Id).ToList(), cancellationToken);
        await ledger.UpsertUnitsAsync(stale, members, cancellationToken);
        return pending + stale.Count;
    }
}
