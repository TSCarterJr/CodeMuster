using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class TypeScriptMapperTests
{
    private const string MissingNode = "codemuster-no-such-node";

    [Fact]
    public void Language_is_typescript()
    {
        Assert.Equal(Languages.TypeScript, new TypeScriptMapper(TestPaths.Node).Language);
    }

    [Fact]
    public async Task Maps_the_fixture_to_the_golden()
    {
        var root = TestPaths.MixedRepoWithTypeScript();

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(root, TestPaths.RepoPaths(root), null, CancellationToken.None);

        GoldenAssert.Matches(map);
    }

    [Fact]
    public void Covers_typescript_and_javascript()
    {
        Assert.Equal([Languages.TypeScript, Languages.JavaScript], new TypeScriptMapper(TestPaths.Node).Languages);
    }

    [Fact]
    public async Task Without_a_config_or_a_script_file_returns_an_empty_map_and_never_starts_node()
    {
        var map = await new TypeScriptMapper(() => throw new InvalidOperationException("node was looked up")).MapAsync(TestPaths.MixedRepo, ["web/tsconfig.base.json", "web/package.json", "README.md"], null, CancellationToken.None);

        Assert.Empty(map.Symbols);
        Assert.Empty(map.Edges);
        Assert.Empty(map.EntryPoints);
        Assert.Equal(0, map.Resolution.Resolved);
        Assert.Equal(0, map.Resolution.Unresolved);
        Assert.Empty(map.Diagnostics);
    }

    [Fact]
    public async Task Missing_node_throws_saying_what_to_install()
    {
        var mapper = new TypeScriptMapper(() => MissingNode);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => mapper.MapAsync(TestPaths.MixedRepo, ["web/tsconfig.json"], null, CancellationToken.None));

        Assert.Contains($"{MissingNode} was not found on PATH", error.Message);
        Assert.Contains("install Node.js", error.Message);
    }

    [Fact]
    public async Task Missing_typescript_package_throws_with_the_npm_command_that_fixes_it()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepo, "web"), "web", "node_modules");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.Contains("typescript was not found for web/tsconfig.json", error.Message);
        Assert.Contains("npm ci --prefix web", error.Message);
    }

    [Fact]
    public async Task A_typescript_without_the_compiler_api_fails_with_one_line_naming_the_fix()
    {
        using var temp = WebWithTypeScript7();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.Equal(
            "TypeScript mapping failed: web/tsconfig.json: typescript 7.0.2 has no JavaScript compiler API; run npm i -D @typescript/typescript6 --prefix web",
            error.Message);
    }

    [Fact]
    public async Task The_typescript6_package_stands_in_for_a_typescript_without_the_compiler_api()
    {
        using var temp = WebWithTypeScript7();
        var realTypeScript = Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web", "node_modules", "typescript");
        temp.Write("web/node_modules/@typescript/typescript6/package.json", """{ "name": "@typescript/typescript6", "version": "6.0.2", "main": "index.js" }""");
        temp.Write("web/node_modules/@typescript/typescript6/index.js", $"module.exports = require({JsonSerializer.Serialize(realTypeScript)});\n");

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        GoldenAssert.Matches(map);
    }

    [Fact]
    public async Task A_typescript_package_that_throws_when_loaded_is_named_in_one_line_with_the_reinstall_command()
    {
        using var temp = WebWithTypeScript7();
        // The real @typescript/typescript6 re-exports @typescript/old; a partial install leaves that one out.
        temp.Write("web/node_modules/@typescript/typescript6/package.json", """{ "name": "@typescript/typescript6", "version": "6.0.2", "main": "lib/typescript.js" }""");
        temp.Write("web/node_modules/@typescript/typescript6/lib/typescript.js", "module.exports = require('@typescript/old');\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.Equal(
            "TypeScript mapping failed: web/tsconfig.json: @typescript/typescript6 could not be loaded (Cannot find module '@typescript/old'); run npm ci --prefix web",
            error.Message);
    }

    [Fact]
    public async Task The_typescript6_package_is_used_when_no_typescript_package_is_installed()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepo, "web"), "web", "node_modules");
        var realTypeScript = Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web", "node_modules", "typescript");
        temp.Write("web/node_modules/@typescript/typescript6/package.json", """{ "name": "@typescript/typescript6", "version": "6.0.2", "main": "index.js" }""");
        temp.Write("web/node_modules/@typescript/typescript6/index.js", $"module.exports = require({JsonSerializer.Serialize(realTypeScript)});\n");

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        GoldenAssert.Matches(map);
    }

    [Fact]
    public async Task A_workspace_member_without_its_own_lockfile_gets_a_hint_that_does_not_fork_the_workspace()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepo, "web"), "web", "node_modules");
        File.Delete(Path.Combine(temp.Root, "web", "package-lock.json"));
        temp.Write("package.json", """{ "name": "root", "private": true, "workspaces": ["web"] }""");
        temp.Write("package-lock.json", """{ "name": "root", "lockfileVersion": 3, "packages": {} }""");
        temp.Write("node_modules/typescript/package.json", """{ "name": "typescript", "version": "7.0.2", "exports": { ".": "./lib/version.cjs" } }""");
        temp.Write("node_modules/typescript/lib/version.cjs", "module.exports = { version: '7.0.2', versionMajorMinor: '7.0' };\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.Equal(
            "TypeScript mapping failed: web/tsconfig.json: typescript 7.0.2 has no JavaScript compiler API; add @typescript/typescript6 as a dev dependency of web with its package manager",
            error.Message);
    }

    [Fact]
    public async Task An_unexpected_failure_is_one_line_that_never_names_the_deleted_temp_script()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepo, "web"), "web", "node_modules");
        temp.Write("web/node_modules/typescript/package.json", """{ "name": "typescript", "version": "5.9.3", "main": "index.js" }""");
        temp.Write("web/node_modules/typescript/index.js", "module.exports = { version: '5.9.3', sys: {}, createProgram() {}, readConfigFile() { throw new Error('config reader broke'); } };\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.StartsWith("TypeScript mapping failed: config reader broke", error.Message);
        Assert.DoesNotContain("codemuster-ts-", error.Message);
        Assert.DoesNotContain('\n', error.Message);
    }

    private const string LooseNote = "javascript: not mapped (no tsconfig, jsconfig or typescript package); files are reviewed whole. Add typescript as a dev dependency to map them";

    [Fact]
    public async Task Loose_javascript_without_a_config_or_a_typescript_package_is_skipped_with_a_note_instead_of_failing()
    {
        using var temp = new TempFolder();
        temp.Write("Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />\n");
        temp.Write("wwwroot/js/site.js", "function toggle() {\n  return 1;\n}\n");

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, ["Web.csproj", "wwwroot/js/site.js"], null, CancellationToken.None);

        Assert.Empty(map.Symbols);
        Assert.Empty(map.Diagnostics);
        Assert.Equal([new SkippedLanguage(Languages.JavaScript, LooseNote)], map.SkippedLanguages);
    }

    [Fact]
    public async Task Loose_javascript_is_skipped_with_a_note_when_node_is_missing()
    {
        var map = await new TypeScriptMapper(() => MissingNode).MapAsync(TestPaths.MixedRepo, ["wwwroot/js/site.js"], null, CancellationToken.None);

        Assert.Empty(map.Symbols);
        Assert.Equal([new SkippedLanguage(Languages.JavaScript, "javascript: not mapped (node was not found on PATH); files are reviewed whole. Install Node.js 22 or later and add typescript as a dev dependency to map them")], map.SkippedLanguages);
    }

    [Fact]
    public async Task A_jsconfig_without_a_typescript_package_still_fails_because_mapping_was_asked_for()
    {
        using var temp = new TempFolder();
        temp.Write("jsconfig.json", "{}");
        temp.Write("src/app.js", "function main() {\n  return 1;\n}\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, ["jsconfig.json", "src/app.js"], null, CancellationToken.None));

        Assert.Contains("typescript was not found for jsconfig.json", error.Message);
    }

    [Fact]
    public async Task A_jsconfig_without_node_still_fails()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(() => MissingNode).MapAsync(TestPaths.MixedRepo, ["jsconfig.json", "src/app.js"], null, CancellationToken.None));

        Assert.Contains($"{MissingNode} was not found on PATH", error.Message);
    }

    [Fact]
    public async Task Typescript_files_without_a_config_or_a_typescript_package_fail_naming_the_npm_command_that_adds_it()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.RepoRoot, "fixtures", "express-js"), ".", "node_modules");
        temp.Write("package-lock.json", """{ "name": "express-js", "lockfileVersion": 3, "packages": {} }""");
        temp.Write("web/lib/format.ts", "export function format(value: number) {\n  return value.toFixed(2);\n}\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None));

        Assert.Equal("TypeScript mapping failed: typescript was not found for the JavaScript and TypeScript files; run npm i -D typescript", error.Message);
    }

    [Fact]
    public async Task A_typescript_folder_without_a_lockfile_is_told_to_add_typescript_with_its_package_manager()
    {
        using var temp = new TempFolder();
        temp.Write("site/package.json", """{ "name": "site", "private": true }""");
        temp.Write("site/app.ts", "function main() {\n  return 1;\n}\n");

        // Scan excludes package.json as data, so the mapper finds it on disk.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, ["site/app.ts"], null, CancellationToken.None));

        Assert.Equal("TypeScript mapping failed: typescript was not found for the JavaScript and TypeScript files; add typescript as a dev dependency of site with its package manager", error.Message);
    }

    private static TempFolder WebWithTypeScript7()
    {
        var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepo, "web"), "web", "node_modules");
        temp.Write("web/node_modules/typescript/package.json", """{ "name": "typescript", "version": "7.0.2", "exports": { ".": "./lib/version.cjs" } }""");
        temp.Write("web/node_modules/typescript/lib/version.cjs", "module.exports = { version: '7.0.2', versionMajorMinor: '7.0' };\n");
        return temp;
    }
}
