using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Fixes what the audit confirmed (D37): one unit per file with confirmed findings, one fresh agent call each, serially, committing after every file it changes. The only use case that writes to the repository.</summary>
public sealed class Fix(ILedger ledger, ISourceTree? tree = null, IClock? clock = null, Config? config = null, IWorkspace? workspace = null)
{
    /// <summary>Plans the units, then works them one at a time: pack, agent, record, commit. Refuses to start unless the working tree is clean, and throws away a failed attempt's edits before retrying.</summary>
    public async Task<FixResult> RunAsync(IAgentAdapter adapter, FixOptions options, IProgress<string>? notes, CancellationToken cancellationToken)
    {
        var repository = workspace ?? throw new InvalidOperationException("fix needs a workspace");
        if (!await repository.IsCleanAsync(cancellationToken))
        {
            throw new InvalidOperationException("the working tree has uncommitted changes; commit or stash them before codemuster fix");
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
                return new FixResult(units, fixedCount, declined, gaveUp);
            }

            var targets = (await ledger.GetCurrentFindingsAsync(cancellationToken))
                .Count(f => f.Finding.Path == pack.Key && f.Verification?.Verdict == Verdict.Confirmed);
            notes?.Report(string.Create(CultureInfo.InvariantCulture, $"fixing {pack.Key}, {targets} finding(s)"));

            var attempt = attempts.GetValueOrDefault(pack.UnitId) + 1;
            attempts[pack.UnitId] = attempt;
            var result = await AttemptAsync(adapter, done, pack, cancellationToken);
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

    private static async Task<AttemptResult> AttemptAsync(IAgentAdapter adapter, Done done, UnitPack pack, CancellationToken cancellationToken)
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
        var result = await done.RunAsync(pack.UnitId, pack.Fingerprint, json, cancellationToken);
        return new AttemptResult(result.Outcome, result.Outcome == DoneOutcome.Recorded ? json : null);
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
