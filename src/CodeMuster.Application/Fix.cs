using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Fixes what the audit confirmed (D37): one unit per file with confirmed findings, one fresh agent call each, serially, committing after every file it changes. The only use case that writes to the repository.</summary>
public sealed class Fix(ILedger ledger, ISourceTree? tree = null, IClock? clock = null, Config? config = null, IWorkspace? workspace = null, ITestRunner? tests = null)
{
    /// <summary>Plans and fixes confirmed findings. With consent, saves tracked local changes and restores them afterward, including on failure or cancellation.</summary>
    public async Task<FixResult> RunAsync(IAgentAdapter adapter, FixOptions options, IProgress<string>? notes, CancellationToken cancellationToken)
    {
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
        var next = new Next(ledger, tree!, config ?? Config.Default, interactive: false, UnitKind.Fix, options.Path);
        var done = new Done(ledger, clock!, config ?? Config.Default, adapter.Identity);
        var gaveUp = new List<string>();
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var units = 0;
        var fixedCount = 0;
        var declined = 0;

        while (true)
        {
            var pack = (await next.RunAsync(gaveUp.Count + 1, cancellationToken)).FirstOrDefault(p => !gaveUp.Contains(p.UnitId));
            if (pack is null)
            {
                return new FixResult(units, fixedCount, declined, gaveUp, tests is not null);
            }

            var targets = (await ledger.GetCurrentFindingsAsync(cancellationToken))
                .Count(f => f.Finding.Path == pack.Key && f.Verification?.Verdict == Verdict.Confirmed);
            notes?.Report(string.Create(CultureInfo.InvariantCulture, $"fixing {pack.Key}, {targets} finding(s)"));

            var attempt = attempts.GetValueOrDefault(pack.UnitId) + 1;
            attempts[pack.UnitId] = attempt;
            var result = await AttemptAsync(adapter, done, pack, notes, cancellationToken);
            if (result.Outcome != DoneOutcome.Recorded)
            {
                await repository.RestoreAsync(cancellationToken);
                if (attempt >= options.MaxAttempts)
                {
                    gaveUp.Add(pack.UnitId);
                }

                continue;
            }

            var response = FixResponseJson.Parse(result.ResponseJson!);
            units++;
            fixedCount += response.Addressed.Count;
            declined += response.Declined.Count;
            if (!await repository.IsCleanAsync(cancellationToken))
            {
                await repository.CommitAsync(CommitMessage(pack.Key, response), cancellationToken);
            }
        }
    }

    private async Task<AttemptResult> AttemptAsync(IAgentAdapter adapter, Done done, UnitPack pack, IProgress<string>? notes, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await adapter.RunAsync(pack.Markdown, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new AttemptResult(DoneOutcome.Rejected, null);
        }

        var json = ResponseText.ExtractJson(text);
        try
        {
            FixResponseJson.Parse(json);
        }
        catch (JsonException)
        {
            return new AttemptResult(DoneOutcome.InvalidResponse, null);
        }

        if (tests is not null)
        {
            notes?.Report($"running tests for {pack.Key}");
            var run = await tests.RunAsync(cancellationToken).ConfigureAwait(false);
            if (!run.Passed)
            {
                notes?.Report($"tests failed after fixing {pack.Key}, throwing the change away: {LastLine(run.Output)}");
                return new AttemptResult(DoneOutcome.Rejected, null);
            }
        }

        var result = await done.RunAsync(pack.UnitId, pack.Fingerprint, json, cancellationToken);
        return new AttemptResult(result.Outcome, result.Outcome == DoneOutcome.Recorded ? json : null);
    }

    private static string LastLine(string output)
    {
        var lines = output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        return lines.Count == 0 ? "no output" : lines[^1];
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

    private sealed record AttemptResult(DoneOutcome Outcome, string? ResponseJson);

    /// <summary>Creates or refreshes a fix unit for every file with a confirmed finding, and retires fix units whose findings are gone. A unit already fixed against the file's current content keeps its Done status.</summary>
    public async Task<FixPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var confirmed = (await ledger.GetCurrentFindingsAsync(cancellationToken))
            .Where(f => f.Verification?.Verdict == Verdict.Confirmed)
            .ToList();
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
            var status = previous is null || previous.Status == UnitStatus.Retired ? UnitStatus.Pending
                : previous.Status == UnitStatus.Done && previous.Fingerprint != fingerprint ? UnitStatus.Stale
                : previous.Status;
            return new Unit(plan.Id, plan.Kind, plan.Key, fingerprint, status, plan.Fidelity, previous?.LensHash, previous?.Summary, previous?.SummaryHash);
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
}
