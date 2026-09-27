using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// Decides when a scan may reuse the stored code map instead of mapping again (D66). The map is a function of the mapper builds, the files they map with their content,
/// and the build and package files that shape a project; when a hash over all of them equals the one stored with a complete map, mapping again would return the same map.
/// </summary>
public static class MapInputs
{
    private static readonly string[] BuildFiles =
    [
        "directory.build.props", "directory.build.targets", "directory.packages.props", "global.json", "nuget.config",
        "package.json", "package-lock.json", "npm-shrinkwrap.json", "pnpm-lock.yaml", "yarn.lock",
    ];

    /// <summary>
    /// SHA-256 over each mapper's language and build (its assembly's module version id, which changes whenever the mapper's code or embedded script does),
    /// every file handed to the mappers with its content hash, and every tracked build or package file in <paramref name="current"/> (MSBuild props and targets,
    /// global.json, NuGet.config, package.json and lockfiles, and every tsconfig or jsconfig, including ones that are only extended) with its content hash.
    /// </summary>
    public static string Digest(IReadOnlyList<ICodeMapper> mappers, IReadOnlyList<FileRecord> mappingInputs, IReadOnlyList<FileRecord> current)
    {
        var lines = new List<string> { "map inputs 1" };
        lines.AddRange(mappers.Select(m => "mapper " + string.Join(',', m.Languages) + " " + m.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N")));
        lines.AddRange(mappingInputs.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => "mapped " + f.Path + "\0" + f.ContentHash));
        lines.AddRange(current.Where(f => f.DeletedAt is null && IsBuildFile(f.Path)).OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => "input " + f.Path + "\0" + f.ContentHash));
        return Hashing.Sha256Hex(string.Join('\n', lines));
    }

    /// <summary>The digest to store with a freshly mapped map, or null when a later scan must not reuse it: a mapper failed or reported a diagnostic, or skipped a language that has files, so mapping again after a restore or an install could return more.</summary>
    public static string? Storable(string digest, CompositeMap mapped, IReadOnlyList<ICodeMapper> mappers, IReadOnlyList<FileRecord> mappingInputs)
    {
        var present = mappers.SelectMany(m => m.Languages).Where(language => mappingInputs.Any(f => f.Language == language));
        var complete = mapped.FailedLanguages.Count == 0
            && mapped.Map.Diagnostics.Count == 0
            && present.All(mapped.MappedLanguages.Contains);
        return complete ? digest : null;
    }

    /// <summary>The map the mappers returned before the UI-to-API join, rebuilt from a stored map: the join only appended <see cref="EdgeKind.Http"/> edges and <see cref="HttpCall.DiagnosticPrefix"/> diagnostics.</summary>
    public static CompositeMap Unlinked(StoredCodeMap stored) =>
        new(stored.Map with
        {
            Edges = stored.Map.Edges.Where(e => e.Kind != EdgeKind.Http).ToList(),
            Diagnostics = stored.Map.Diagnostics.Where(d => !d.StartsWith(HttpCall.DiagnosticPrefix, StringComparison.Ordinal)).ToList(),
        }, stored.FailedLanguages, stored.MappedLanguages);

    private static bool IsBuildFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return BuildFiles.Contains(name)
            || name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith(".targets", StringComparison.Ordinal)
            || (name.StartsWith("tsconfig", StringComparison.Ordinal) || name.StartsWith("jsconfig", StringComparison.Ordinal)) && name.EndsWith(".json", StringComparison.Ordinal);
    }
}
