using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Rejects browser receipts and repairs after tracked source or review settings change without a scan.</summary>
public static class UxSourceSnapshot
{
    /// <summary>Checks current tracked source against the UX unit's repository-wide context fingerprint.</summary>
    public static async Task CheckAsync(ILedger ledger, ISourceTree tree, IContentHasher hasher, Config config, string target, CancellationToken cancellationToken)
    {
        if (!config.UserExperience.Applies(target)) throw new JsonException("UX review is disabled or excluded for this target");
        var prior = (await ledger.GetFilesAsync(cancellationToken)).Where(f => f.DeletedAt is null && f.ExcludedReason is null).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var files = (await tree.ListFilesAsync(cancellationToken)).Where(f => config.ExcludedReason(f.Path, f.LinguistGenerated) is null).ToList();
        if (!prior.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(files.Select(f => f.Path)))
            throw new JsonException("tracked source inventory changed since scan; scan and inspect the current application again");
        var hashes = await ContentHashes.CurrentAsync(hasher, files, prior, cancellationToken);
        var current = files.Select(file => prior[file.Path] with { ContentHash = hashes[file.Path] }).ToList();
        var planned = UxReview.Plan(current, config.UserExperience).SingleOrDefault(p => p.Key == target);
        var unit = await ledger.GetUnitAsync(UnitIds.Ux(target), cancellationToken);
        if (planned is null || unit is null || unit.Status == UnitStatus.Retired || Fingerprints.Compute(planned.Members) != unit.Fingerprint)
            throw new JsonException("source or UX settings changed since scan; scan and inspect the current application again");
    }
}
