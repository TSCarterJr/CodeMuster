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
    public async Task Without_a_tsconfig_json_returns_an_empty_map_and_never_starts_node()
    {
        var map = await new TypeScriptMapper(() => throw new InvalidOperationException("node was looked up")).MapAsync(TestPaths.MixedRepo, ["web/tsconfig.base.json", "web/lib/api.ts"], null, CancellationToken.None);

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
}
