using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Enforces report-only categories and the freshness required for browser-based repairs.</summary>
public static class ReviewEligibility
{
    /// <summary>False for unused-code candidates and subjective UX recommendations regardless of verification.</summary>
    public static bool CanAutoFix(Finding finding) =>
        DeadCodeReview.CanAutoFix(finding)
        && !string.Equals(finding.Category.Trim(), "ux_recommendation", StringComparison.OrdinalIgnoreCase);

    /// <summary>Also checks the original unit so mislabeled or stale browser observations cannot authorize repairs.</summary>
    public static bool CanAutoFix(UnitFinding finding, Unit? source) =>
        source?.Kind != UnitKind.DeadCode
        && CanAutoFix(finding.Finding)
        && (!RequiresBrowser(finding.Finding, source)
            || source is { Status: UnitStatus.Done } && source.Fingerprint == finding.Fingerprint);

    /// <summary>Also requires browser scope and lens settings to match the source review that authorized a repair.</summary>
    public static bool CanAutoFix(UnitFinding finding, Unit? source, Config config) =>
        CanAutoFix(finding, source)
        && (!RequiresBrowser(finding.Finding, source)
            || config.UserExperience.Applies(finding.Finding.Path)
                && source?.LensHash == Config.HashOf(config.LensesFor([(finding.Finding.Path, Languages.FromPath(finding.Finding.Path))])));

    /// <summary>True for browser-derived findings even when one classification field was mislabeled.</summary>
    public static bool RequiresBrowser(Finding finding, Unit? source) =>
        source?.Kind == UnitKind.Ux
        || string.Equals(finding.LensId.Trim(), "user_experience", StringComparison.OrdinalIgnoreCase)
        || finding.Category.Trim().StartsWith("ux_", StringComparison.OrdinalIgnoreCase);
}
