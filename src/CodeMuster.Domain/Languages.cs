namespace CodeMuster.Domain;

/// <summary>Language identifiers stored in the ledger and the extension map that assigns them.</summary>
public static class Languages
{
    /// <summary>C# source.</summary>
    public const string CSharp = "csharp";

    /// <summary>TypeScript source, including declaration files.</summary>
    public const string TypeScript = "typescript";

    /// <summary>JavaScript source.</summary>
    public const string JavaScript = "javascript";

    /// <summary>Razor components and views.</summary>
    public const string Razor = "razor";

    /// <summary>JSON documents.</summary>
    public const string Json = "json";

    /// <summary>YAML documents.</summary>
    public const string Yaml = "yaml";

    /// <summary>Markdown documents.</summary>
    public const string Markdown = "markdown";

    /// <summary>SQL scripts.</summary>
    public const string Sql = "sql";

    /// <summary>CSS and SCSS stylesheets.</summary>
    public const string Css = "css";

    /// <summary>HTML documents.</summary>
    public const string Html = "html";

    /// <summary>POSIX shell scripts.</summary>
    public const string Shell = "shell";

    /// <summary>PowerShell scripts and modules.</summary>
    public const string PowerShell = "powershell";

    /// <summary>Python source.</summary>
    public const string Python = "python";

    /// <summary>Go source.</summary>
    public const string Go = "go";

    /// <summary>Rust source.</summary>
    public const string Rust = "rust";

    /// <summary>Java source.</summary>
    public const string Java = "java";

    /// <summary>Any file whose extension is not in the map, including files with no extension.</summary>
    public const string Unknown = "unknown";

    /// <summary>Returns the language for a path by its file extension, case-insensitively; unknown when there is no mapping.</summary>
    public static string FromPath(string path)
    {
        return Path.GetExtension(RepoPath.Normalize(path)).ToLowerInvariant() switch
        {
            ".cs" => CSharp,
            ".ts" or ".tsx" or ".mts" or ".cts" => TypeScript,
            ".js" or ".jsx" or ".mjs" or ".cjs" => JavaScript,
            ".razor" or ".cshtml" => Razor,
            ".json" => Json,
            ".yml" or ".yaml" => Yaml,
            ".md" => Markdown,
            ".sql" => Sql,
            ".css" or ".scss" => Css,
            ".html" or ".htm" => Html,
            ".sh" or ".bash" => Shell,
            ".ps1" or ".psm1" => PowerShell,
            ".py" => Python,
            ".go" => Go,
            ".rs" => Rust,
            ".java" => Java,
            _ => Unknown,
        };
    }
}
