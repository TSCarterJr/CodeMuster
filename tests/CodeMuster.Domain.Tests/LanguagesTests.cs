using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class LanguagesTests
{
    [Theory]
    [InlineData(Languages.CSharp, "csharp")]
    [InlineData(Languages.TypeScript, "typescript")]
    [InlineData(Languages.JavaScript, "javascript")]
    [InlineData(Languages.Razor, "razor")]
    [InlineData(Languages.Json, "json")]
    [InlineData(Languages.Yaml, "yaml")]
    [InlineData(Languages.Markdown, "markdown")]
    [InlineData(Languages.Sql, "sql")]
    [InlineData(Languages.Css, "css")]
    [InlineData(Languages.Html, "html")]
    [InlineData(Languages.Shell, "shell")]
    [InlineData(Languages.PowerShell, "powershell")]
    [InlineData(Languages.Python, "python")]
    [InlineData(Languages.Go, "go")]
    [InlineData(Languages.Rust, "rust")]
    [InlineData(Languages.Java, "java")]
    [InlineData(Languages.Unknown, "unknown")]
    public void Constants_are_the_ledger_language_ids(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("src/Program.cs", Languages.CSharp)]
    [InlineData("src/Program.CS", Languages.CSharp)]
    [InlineData(@"src\Program.cs", Languages.CSharp)]
    [InlineData("web/app.ts", Languages.TypeScript)]
    [InlineData("web/app/page.tsx", Languages.TypeScript)]
    [InlineData("web/config.mts", Languages.TypeScript)]
    [InlineData("web/config.cts", Languages.TypeScript)]
    [InlineData("web/types/react.d.ts", Languages.TypeScript)]
    [InlineData("web/app.js", Languages.JavaScript)]
    [InlineData("web/App.jsx", Languages.JavaScript)]
    [InlineData("web/config.mjs", Languages.JavaScript)]
    [InlineData("web/config.cjs", Languages.JavaScript)]
    [InlineData("src/Pages/Index.razor", Languages.Razor)]
    [InlineData("src/Views/Home/Index.cshtml", Languages.Razor)]
    [InlineData("appsettings.json", Languages.Json)]
    [InlineData(".github/workflows/test.yml", Languages.Yaml)]
    [InlineData("docker-compose.yaml", Languages.Yaml)]
    [InlineData("README.md", Languages.Markdown)]
    [InlineData("db/schema.sql", Languages.Sql)]
    [InlineData("web/styles.css", Languages.Css)]
    [InlineData("web/styles.scss", Languages.Css)]
    [InlineData("web/index.html", Languages.Html)]
    [InlineData("web/index.htm", Languages.Html)]
    [InlineData("scripts/build.sh", Languages.Shell)]
    [InlineData("scripts/build.bash", Languages.Shell)]
    [InlineData("scripts/build.ps1", Languages.PowerShell)]
    [InlineData("scripts/Tools.psm1", Languages.PowerShell)]
    [InlineData("tools/gen.py", Languages.Python)]
    [InlineData("cmd/main.go", Languages.Go)]
    [InlineData("src/main.rs", Languages.Rust)]
    [InlineData("src/Main.java", Languages.Java)]
    [InlineData("README", Languages.Unknown)]
    [InlineData("Makefile", Languages.Unknown)]
    [InlineData("src/v1.0/README", Languages.Unknown)]
    [InlineData(".gitignore", Languages.Unknown)]
    [InlineData("notes.txt", Languages.Unknown)]
    [InlineData("", Languages.Unknown)]
    public void FromPath_maps_extension_to_language(string path, string expected)
    {
        Assert.Equal(expected, Languages.FromPath(path));
    }
}
