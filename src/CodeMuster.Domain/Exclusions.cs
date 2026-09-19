using System.Collections.Frozen;

namespace CodeMuster.Domain;

/// <summary>Decides which files are never analyzed and names the reason stored in the ledger's excluded_reason column.</summary>
public static class Exclusions
{
    private static readonly FrozenSet<string> LockFiles = new[]
    {
        "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "packages.lock.json",
        "Cargo.lock", "go.sum", "poetry.lock", "Pipfile.lock", "Gemfile.lock", "composer.lock",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> BinaryExtensions = new[]
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff", ".pdf",
        ".zip", ".gz", ".tgz", ".tar", ".7z", ".rar", ".dll", ".exe", ".so", ".dylib", ".pdb",
        ".woff", ".woff2", ".ttf", ".otf", ".eot", ".mp3", ".mp4", ".wav", ".mov", ".avi",
        ".jar", ".class", ".nupkg", ".snupkg", ".bin", ".dat",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DocumentExtensions = new[]
    {
        ".md", ".markdown", ".mdown", ".mkd", ".txt", ".text", ".rst", ".rest", ".adoc", ".asciidoc",
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".rtf", ".odt", ".ott", ".pages", ".tex", ".epub",
        ".ppt", ".pptx", ".pptm", ".odp", ".key", ".xls", ".xlsx", ".xlsm", ".ods", ".numbers",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DataExtensions = new[]
    {
        ".json", ".jsonc", ".json5", ".jsonl", ".ndjson", ".geojson", ".csv", ".tsv", ".xml",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".config", ".env", ".properties",
        ".resx", ".resources", ".log", ".sln", ".slnx", ".csproj", ".fsproj", ".vbproj",
        ".vcxproj", ".props", ".targets", ".plist", ".parquet", ".arrow", ".avro",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DatabaseExtensions = new[]
    {
        ".db", ".db3", ".sqlite", ".sqlite3", ".s3db", ".mdb", ".accdb", ".dbf", ".realm",
        ".mdf", ".ndf", ".ldf", ".rdb", ".db-wal", ".db-shm", ".db-journal",
        ".sqlite-wal", ".sqlite-shm", ".sqlite-journal", ".sqlite3-wal", ".sqlite3-shm", ".sqlite3-journal",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DocumentNames = new[]
    {
        "README", "LICENSE", "LICENCE", "COPYING", "NOTICE", "CHANGELOG", "CHANGES", "AUTHORS", "CONTRIBUTORS",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DataNames = new[]
    {
        ".gitignore", ".gitattributes", ".gitmodules", ".editorconfig", ".npmrc", ".yarnrc", ".nvmrc",
        ".prettierrc", ".eslintrc", ".babelrc", ".browserslistrc", ".env",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns null when the file should be analyzed, otherwise the first matching reason code in fixed precedence.</summary>
    public static string? Reason(string path, bool linguistGenerated)
    {
        if (linguistGenerated)
        {
            return "linguist-generated";
        }

        var normalized = RepoPath.Normalize(path);
        if (normalized.StartsWith(".codemuster/", StringComparison.Ordinal))
        {
            return "tool-config";
        }

        var slash = normalized.LastIndexOf('/');
        string[] directories = slash < 0 ? [] : normalized[..slash].Split('/');
        var name = normalized[(slash + 1)..];
        var extension = Path.GetExtension(name).ToLowerInvariant();

        if (directories.Contains("Migrations"))
        {
            return "migrations";
        }

        if (EndsWithAny(name, ".g.cs", ".g.i.cs", ".Designer.cs"))
        {
            return "generated";
        }

        if (EndsWithAny(name, ".min.js", ".min.css"))
        {
            return "minified";
        }

        if (LockFiles.Contains(name))
        {
            return "lockfile";
        }

        if (EndsWithAny(name, ".d.ts", ".d.mts", ".d.cts"))
        {
            return "declarations";
        }

        if (extension == ".snap" || directories.Contains("__snapshots__"))
        {
            return "snapshot";
        }

        if (BinaryExtensions.Contains(extension))
        {
            return "binary";
        }

        if (DocumentExtensions.Contains(extension) || DocumentNames.Contains(name))
        {
            return "documentation";
        }

        if (DataExtensions.Contains(extension) || DataNames.Contains(name) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
        {
            return "data";
        }

        if (DatabaseExtensions.Contains(extension))
        {
            return "database";
        }

        return null;
    }

    private static bool EndsWithAny(string name, params string[] suffixes)
    {
        return suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
