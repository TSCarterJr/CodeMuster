using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class ExclusionsTests
{
    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".editorconfig")]
    [InlineData(".npmrc")]
    [InlineData(".env")]
    [InlineData(".env.production")]
    public void ExtensionlessConfiguration_IsExcluded(string path) =>
        Assert.Equal("data", Exclusions.Reason(path, false));

    [Theory]
    [InlineData("md", "documentation")]
    [InlineData("markdown", "documentation")]
    [InlineData("txt", "documentation")]
    [InlineData("rst", "documentation")]
    [InlineData("adoc", "documentation")]
    [InlineData("doc", "documentation")]
    [InlineData("docx", "documentation")]
    [InlineData("rtf", "documentation")]
    [InlineData("odt", "documentation")]
    [InlineData("pptx", "documentation")]
    [InlineData("xlsx", "documentation")]
    [InlineData("json", "data")]
    [InlineData("jsonc", "data")]
    [InlineData("json5", "data")]
    [InlineData("jsonl", "data")]
    [InlineData("ndjson", "data")]
    [InlineData("csv", "data")]
    [InlineData("tsv", "data")]
    [InlineData("xml", "data")]
    [InlineData("yaml", "data")]
    [InlineData("yml", "data")]
    [InlineData("toml", "data")]
    [InlineData("ini", "data")]
    [InlineData("config", "data")]
    [InlineData("sln", "data")]
    [InlineData("csproj", "data")]
    [InlineData("db", "database")]
    [InlineData("sqlite", "database")]
    [InlineData("sqlite3", "database")]
    [InlineData("db3", "database")]
    [InlineData("mdb", "database")]
    [InlineData("accdb", "database")]
    [InlineData("db-wal", "database")]
    [InlineData("sqlite-shm", "database")]
    public void NonCodeFormats_AreExcludedAtAnyDepthAndCase(string extension, string reason)
    {
        Assert.Equal(reason, Exclusions.Reason("file." + extension, false));
        Assert.Equal(reason, Exclusions.Reason(@"nested\FILE." + extension.ToUpperInvariant(), false));
    }

    [Theory]
    [InlineData("README")]
    [InlineData("LICENSE")]
    [InlineData("docs/CHANGELOG")]
    [InlineData("NOTICE")]
    public void ExtensionlessDocuments_AreExcluded(string path) =>
        Assert.Equal("documentation", Exclusions.Reason(path, false));

    [Theory]
    [InlineData("src/App.vue")]
    [InlineData("src/App.svelte")]
    [InlineData("src/page.mdx")]
    [InlineData("web/page.html")]
    [InlineData("db/query.sql")]
    [InlineData("scripts/build.sh")]
    [InlineData("scripts/build.ps1")]
    [InlineData("Dockerfile")]
    [InlineData("Makefile")]
    public void SourceAndExecutableTemplates_RemainIncluded(string path) =>
        Assert.Null(Exclusions.Reason(path, false));

    [Theory]
    [InlineData(@"src\Data\Migrations\20260101_Initial.cs", "migrations")]
    [InlineData("src/Data/Migrations/20260101_Initial.cs", "migrations")]
    [InlineData("Migrations/Initial.cs", "migrations")]
    [InlineData(".codemuster/config.json", "tool-config")]
    [InlineData(".codemuster/ledger.db", "tool-config")]
    [InlineData("src/.codemuster/notes.md", "documentation")]
    [InlineData("src/Data/QuoteDto.g.cs", "generated")]
    [InlineData("src/Data/QuoteDto.g.i.cs", "generated")]
    [InlineData("src/Forms/Form1.Designer.cs", "generated")]
    [InlineData("src/Forms/Form1.DESIGNER.CS", "generated")]
    [InlineData("src/Data/QuoteDto.G.CS", "generated")]
    [InlineData("web/dist/app.min.js", "minified")]
    [InlineData("web/dist/app.min.css", "minified")]
    [InlineData("web/package-lock.json", "lockfile")]
    [InlineData("web/types/react.d.ts", "declarations")]
    [InlineData("web/types/react.d.mts", "declarations")]
    [InlineData("web/types/react.d.cts", "declarations")]
    [InlineData("web/__snapshots__/a.snap", "snapshot")]
    [InlineData("web/__snapshots__/a.js.snap", "snapshot")]
    [InlineData("web/__snapshots__/a.txt", "snapshot")]
    [InlineData("a/b.snap", "snapshot")]
    [InlineData("assets/logo.png", "binary")]
    [InlineData("assets/LOGO.PNG", "binary")]
    [InlineData("src/Program.cs", null)]
    [InlineData("src/Migrations.cs", null)]
    [InlineData("src/MigrationsHelper/a.cs", null)]
    [InlineData("src/migrations/a.cs", null)]
    [InlineData("web/app.ts", null)]
    [InlineData("web/app.js", null)]
    [InlineData("web/styles.css", null)]
    [InlineData("web/package.json", "data")]
    [InlineData("README.md", "documentation")]
    [InlineData("AGENTS.md", "documentation")]
    [InlineData("docs/guide.MD", "documentation")]
    [InlineData(@"src\docs\guide.Md", "documentation")]
    [InlineData("web/page.mdx", null)]
    [InlineData("README", "documentation")]
    [InlineData("src/Snapshots/a.cs", null)]
    public void Reason_returns_first_matching_code_or_null(string path, string? expected)
    {
        Assert.Equal(expected, Exclusions.Reason(path, linguistGenerated: false));
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("src/Data/QuoteDto.g.cs")]
    [InlineData("src/Data/Migrations/20260101_Initial.cs")]
    [InlineData("assets/logo.png")]
    public void Reason_puts_linguist_generated_before_every_other_rule(string path)
    {
        Assert.Equal("linguist-generated", Exclusions.Reason(path, linguistGenerated: true));
    }

    [Theory]
    [InlineData("src/Migrations/Foo.g.cs", "migrations")]
    [InlineData("web/Migrations/package-lock.json", "migrations")]
    [InlineData("web/dist/Form1.Designer.cs", "generated")]
    [InlineData("web/__snapshots__/app.min.js", "minified")]
    [InlineData("web/__snapshots__/package-lock.json", "lockfile")]
    [InlineData("web/__snapshots__/react.d.ts", "declarations")]
    [InlineData("web/__snapshots__/logo.png", "snapshot")]
    public void Reason_follows_the_documented_precedence(string path, string expected)
    {
        Assert.Equal(expected, Exclusions.Reason(path, linguistGenerated: false));
    }

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("npm-shrinkwrap.json")]
    [InlineData("yarn.lock")]
    [InlineData("pnpm-lock.yaml")]
    [InlineData("packages.lock.json")]
    [InlineData("Cargo.lock")]
    [InlineData("go.sum")]
    [InlineData("poetry.lock")]
    [InlineData("Pipfile.lock")]
    [InlineData("Gemfile.lock")]
    [InlineData("composer.lock")]
    public void Reason_recognizes_every_lockfile(string name)
    {
        Assert.Equal("lockfile", Exclusions.Reason("web/" + name, linguistGenerated: false));
        Assert.Equal("lockfile", Exclusions.Reason(name, linguistGenerated: false));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("ico")]
    [InlineData("webp")]
    [InlineData("tif")]
    [InlineData("tiff")]
    [InlineData("pdf")]
    [InlineData("zip")]
    [InlineData("gz")]
    [InlineData("tgz")]
    [InlineData("tar")]
    [InlineData("7z")]
    [InlineData("rar")]
    [InlineData("dll")]
    [InlineData("exe")]
    [InlineData("so")]
    [InlineData("dylib")]
    [InlineData("pdb")]
    [InlineData("woff")]
    [InlineData("woff2")]
    [InlineData("ttf")]
    [InlineData("otf")]
    [InlineData("eot")]
    [InlineData("mp3")]
    [InlineData("mp4")]
    [InlineData("wav")]
    [InlineData("mov")]
    [InlineData("avi")]
    [InlineData("jar")]
    [InlineData("class")]
    [InlineData("nupkg")]
    [InlineData("snupkg")]
    [InlineData("bin")]
    [InlineData("dat")]
    public void Reason_recognizes_every_binary_extension(string extension)
    {
        Assert.Equal("binary", Exclusions.Reason("assets/file." + extension, linguistGenerated: false));
        Assert.Equal("binary", Exclusions.Reason("assets/FILE." + extension.ToUpperInvariant(), linguistGenerated: false));
    }
}
