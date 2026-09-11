using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Records the model's response for one unit: validates it against the unit's members, then stores the analysis and findings.</summary>
public sealed class Done(ILedger ledger, IClock clock, Config config)
{
    /// <summary>Stores the response for <paramref name="unitId"/> when it still has <paramref name="fingerprint"/> and every finding cites a member path.</summary>
    public async Task<DoneResult> RunAsync(string unitId, string fingerprint, string responseJson, CancellationToken cancellationToken)
    {
        var unit = await ledger.GetUnitAsync(unitId, cancellationToken);
        if (unit is null)
        {
            return new DoneResult(DoneOutcome.Rejected, $"unknown unit {unitId}");
        }

        if (unit.Fingerprint != fingerprint)
        {
            return new DoneResult(DoneOutcome.Rejected, $"unit {unitId} changed since next; run next again");
        }

        var members = await ledger.GetMembersAsync([unitId], cancellationToken);
        var lensHash = Config.HashOf(config.LensesFor(members.Select(m => (m.Path, Languages.FromPath(m.Path)))));
        var now = Timestamps.Format(clock.UtcNow);

        AnalysisResponse response;
        try
        {
            response = AnalysisResponseJson.Parse(responseJson);
        }
        catch (JsonException ex)
        {
            await ledger.RecordAnalysisAsync(new Analysis(unitId, fingerprint, lensHash, now, false, null, ex.Message), [], cancellationToken);
            return new DoneResult(DoneOutcome.InvalidResponse, $"invalid response: {ex.Message}");
        }

        var paths = members.Select(m => m.Path).ToHashSet(StringComparer.Ordinal);
        var findings = response.Findings.Select(f => f with { Path = RepoPath.Normalize(f.Path) }).ToList();
        var outside = findings.FirstOrDefault(f => !paths.Contains(f.Path));
        if (outside is not null)
        {
            return new DoneResult(DoneOutcome.Rejected, $"finding cites {outside.Path}, which is not in unit {unitId}");
        }

        await ledger.RecordAnalysisAsync(new Analysis(unitId, fingerprint, lensHash, now, true, response.Summary, null), findings, cancellationToken);
        return new DoneResult(DoneOutcome.Recorded, string.Create(CultureInfo.InvariantCulture, $"recorded {findings.Count} finding(s) for {unitId}"));
    }
}
