namespace CodeMuster.Domain;

/// <summary>One advisory against one package, as an ecosystem's own audit tool reported it (D38).</summary>
/// <param name="Package">Package name.</param>
/// <param name="VulnerableVersions">The versions the advisory covers, in the tool's own words.</param>
/// <param name="Severity">Advisory severity, mapped onto the findings scale.</param>
/// <param name="AdvisoryId">The advisory's identifier, such as a GHSA id, or its numeric id when that is all the tool gives.</param>
/// <param name="AdvisoryUrl">Where to read it.</param>
/// <param name="Title">The advisory's one-line description.</param>
/// <param name="FixedVersion">The version or range that fixes it, when the tool says; NuGet's audit does not.</param>
/// <param name="Direct">True when the manifest declares this package itself, false when it arrives through another package.</param>
public sealed record VulnerablePackage(
    string Package,
    string VulnerableVersions,
    Severity Severity,
    string AdvisoryId,
    string AdvisoryUrl,
    string Title,
    string? FixedVersion,
    bool Direct);

/// <summary>How a dependency finding is labelled, so everything that reads findings agrees (D38).</summary>
public static class DependencyFindings
{
    /// <summary>The <c>category</c> every dependency finding carries.</summary>
    public const string Category = "dependency";

    /// <summary>The <c>lens_id</c> every dependency finding carries; no lens text produced it.</summary>
    public const string Lens = "dependencies";
}
