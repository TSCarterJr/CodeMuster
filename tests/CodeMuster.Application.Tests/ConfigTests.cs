using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class ConfigTests
{
    private static readonly Lens Tenancy = new("tenancy", "Every query must filter by tenant.", ["src/**/*.cs"], [Languages.CSharp]);

    [Fact]
    public void LensHash_ChangesWithText_AndIsStableOtherwise()
    {
        var same = Tenancy with { };

        Assert.Equal(Tenancy.Hash(), same.Hash());
        Assert.NotEqual(Tenancy.Hash(), (Tenancy with { Instructions = "Every query must filter by tenant!" }).Hash());
        Assert.NotEqual(Tenancy.Hash(), (Tenancy with { Id = "tenant" }).Hash());
        Assert.NotEqual(Tenancy.Hash(), (Tenancy with { Globs = ["web/**"] }).Hash());
        Assert.Equal(64, Tenancy.Hash().Length);
    }

    [Fact]
    public void LensApplies_FiltersByGlobAndLanguage()
    {
        Assert.True(Tenancy.Applies("src/Data/QuoteRepository.cs", Languages.CSharp));
        Assert.False(Tenancy.Applies("web/lib/api.ts", Languages.TypeScript));
        Assert.False(Tenancy.Applies("src/notes.md", Languages.Markdown));
        Assert.True(Config.Default.Lenses[0].Applies("anything/at/all.txt", Languages.Unknown));
    }

    [Fact]
    public void LensesFor_ReturnsEveryLensThatAppliesToAnyFile_OrderedById()
    {
        var config = new Config([Tenancy, Config.Default.Lenses[0]]);

        var lenses = config.LensesFor([("web/lib/api.ts", Languages.TypeScript)]);
        Assert.Equal(["default"], lenses.Select(l => l.Id));

        lenses = config.LensesFor([("web/lib/api.ts", Languages.TypeScript), ("src/A.cs", Languages.CSharp)]);
        Assert.Equal(["default", "tenancy"], lenses.Select(l => l.Id));
    }

    [Fact]
    public void HashOf_IsOrderIndependent_AndTracksLensContent()
    {
        var forward = Config.HashOf([Tenancy, Config.Default.Lenses[0]]);
        var reversed = Config.HashOf([Config.Default.Lenses[0], Tenancy]);

        Assert.Equal(forward, reversed);
        Assert.NotEqual(forward, Config.HashOf([Tenancy]));
        Assert.NotEqual(forward, Config.HashOf([Tenancy with { Instructions = "changed" }, Config.Default.Lenses[0]]));
        Assert.Equal(64, Config.HashOf([]).Length);
    }

    [Fact]
    public void Json_RoundTrips_AndRejectsMissingLenses()
    {
        var text = ConfigJson.Serialize(new Config([Tenancy]));
        var parsed = ConfigJson.Parse(text);

        Assert.Equal(Tenancy, parsed.Lenses[0] with { Globs = Tenancy.Globs, Languages = Tenancy.Languages });
        Assert.Equal(["src/**/*.cs"], parsed.Lenses[0].Globs);
        Assert.Contains("\"lenses\"", text);
        Assert.Throws<JsonException>(() => ConfigJson.Parse("{}"));
    }

    [Fact]
    public void Json_WritesBudgetAndThreshold_AndDefaultsThemForOlderConfigs()
    {
        var text = ConfigJson.Serialize(Config.Default);
        var older = ConfigJson.Parse("""{ "lenses": [ { "id": "default", "instructions": "x", "globs": [], "languages": [] } ] }""");

        Assert.Contains("\"slice_token_budget\": 24000", text);
        Assert.Contains("\"resolution_threshold\": 0.9", text);
        Assert.Equal(24000, older.SliceTokenBudget);
        Assert.Equal(0.9, older.ResolutionThreshold);
    }

    [Fact]
    public async Task Loader_ThrowsNotInitialized_WhenNoConfigFile()
    {
        var loader = new ConfigLoader(new FakeFileSystem());

        var error = await Assert.ThrowsAsync<NotInitializedException>(() => loader.LoadAsync("/repo", CancellationToken.None));

        Assert.Equal("not set up here; run `codemuster init`", error.Message);
    }

    [Fact]
    public async Task Loader_ParsesConfigFile_WhenPresent()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.Files[ConfigLoader.PathFor("/repo")] = ConfigJson.Serialize(new Config([Tenancy]));
        var loader = new ConfigLoader(fileSystem);

        var config = await loader.LoadAsync("/repo", CancellationToken.None);

        Assert.Equal("tenancy", config.Lenses[0].Id);
        Assert.EndsWith(".codemuster/config.json", ConfigLoader.PathFor("/repo").Replace('\\', '/'));
    }
}
