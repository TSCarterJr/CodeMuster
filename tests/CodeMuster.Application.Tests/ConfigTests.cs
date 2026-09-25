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
    public void Json_WritesVerify_AndDefaultsItOnForOlderConfigs()
    {
        const string lenses = """ "lenses": [ { "id": "default", "instructions": "x", "globs": [], "languages": [] } ] """;

        Assert.Contains("\"verify\": true", ConfigJson.Serialize(Config.Default));
        Assert.True(ConfigJson.Parse("{" + lenses + "}").Verify);
        Assert.False(ConfigJson.Parse("{" + lenses + ", \"verify\": false }").Verify);
    }

    [Fact]
    public void Json_WritesExclude_AndDefaultsItEmptyForOlderConfigs()
    {
        const string lenses = """ "lenses": [ { "id": "default", "instructions": "x", "globs": [], "languages": [] } ] """;

        Assert.Contains("\"exclude\": []", ConfigJson.Serialize(Config.Default));
        Assert.Empty(ConfigJson.Parse("{" + lenses + "}").Exclude);
        Assert.Equal(["web/**"], ConfigJson.Parse("{" + lenses + ", \"exclude\": [\"web/**\"] }").Exclude);
    }

    [Fact]
    public void Json_WritesAutomation_AndDefaultsToUpdateForOlderConfigs()
    {
        var older = ConfigJson.Parse("""{ "lenses": [] }""");

        Assert.Contains("\"automation\": \"update\"", ConfigJson.Serialize(Config.Default));
        Assert.Contains("\"automation\": \"update\"", ConfigJson.Serialize(older));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("update")]
    [InlineData("review")]
    [InlineData("review_and_fix")]
    public void Json_RoundTripsExplicitAutomationModes(string mode)
    {
        var config = ConfigJson.Parse("{\"lenses\": [], \"automation\": \"" + mode + "\"}");

        using var serialized = JsonDocument.Parse(ConfigJson.Serialize(config));

        Assert.Equal(mode, serialized.RootElement.GetProperty("automation").GetString());
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("\"Review\"")]
    [InlineData("\" review \"")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Json_RejectsInvalidAutomationWithAllowedModes(string value)
    {
        var error = Assert.Throws<JsonException>(() => ConfigJson.Parse("{\"lenses\": [], \"automation\": " + value + "}"));

        Assert.Contains("automation", error.Message);
        Assert.Contains("off, update, review, review_and_fix", error.Message);
    }

    [Theory]
    [InlineData("{\"lenses\": [], \"exclude\": [\"pkg1/**\", null]}", "exclude")]
    [InlineData("{\"lenses\": [], \"exclude\": [\"*\", \" \"]}", "exclude")]
    [InlineData("{\"lenses\": [{\"id\": \"default\", \"instructions\": \"x\", \"globs\": [\"src/**\", null], \"languages\": []}]}", "globs")]
    public void Json_RejectsNullOrBlankGlobsNamingTheKey(string json, string key)
    {
        var error = Assert.Throws<JsonException>(() => ConfigJson.Parse(json));

        Assert.Contains(key, error.Message);
    }

    [Fact]
    public void ExcludedReason_PutsBuiltInRulesFirst_ThenTheFirstMatchingExcludeGlob()
    {
        var config = Config.Default with { Exclude = ["web/**", "*.ts"] };

        Assert.Equal("migrations", config.ExcludedReason("web/Migrations/Initial.cs", linguistGenerated: false));
        Assert.Equal("linguist-generated", config.ExcludedReason("web/lib/api.ts", linguistGenerated: true));
        Assert.Equal("exclude:web/**", config.ExcludedReason("web/lib/api.ts", linguistGenerated: false));
        Assert.Equal("exclude:*.ts", config.ExcludedReason("tools/gen.ts", linguistGenerated: false));
        Assert.Null(config.ExcludedReason("src/A.cs", linguistGenerated: false));
        Assert.Null(Config.Default.ExcludedReason("web/lib/api.ts", linguistGenerated: false));
    }

    [Fact]
    public void AffectsScan_CountsReviewedFilesAndTheProjectFilesAndManifestsScanReads()
    {
        var config = Config.Default with { Exclude = ["legacy/**"] };

        Assert.True(config.AffectsScan("src/A.cs", linguistGenerated: false));
        Assert.False(config.AffectsScan("src/A.cs", linguistGenerated: true));
        Assert.True(config.AffectsScan("App.sln", linguistGenerated: false));
        Assert.True(config.AffectsScan("src/Api/Api.csproj", linguistGenerated: false));
        Assert.True(config.AffectsScan("web/tsconfig.json", linguistGenerated: false));
        Assert.True(config.AffectsScan("web/package.json", linguistGenerated: false));
        Assert.False((config with { Vulnerabilities = false }).AffectsScan("web/package.json", linguistGenerated: false));
        Assert.False(config.AffectsScan("web/package-lock.json", linguistGenerated: false));
        Assert.False(config.AffectsScan("README.md", linguistGenerated: false));
        Assert.False(config.AffectsScan("web/appsettings.json", linguistGenerated: false));
        Assert.False(config.AffectsScan("legacy/package.json", linguistGenerated: false));
        Assert.False(config.AffectsScan("legacy/Old.csproj", linguistGenerated: false));
    }

    [Fact]
    public void ExcludedReason_MatchesGlobstarDotSlashQuestionMarkAndCaseAsBefore()
    {
        var config = Config.Default with { Exclude = ["./web/**", "**/fixtures/**", "*.Gen.cs", "src/?/*.sql"] };

        Assert.Equal("exclude:./web/**", config.ExcludedReason("web/app/page.tsx", linguistGenerated: false));
        Assert.Equal("exclude:./web/**", config.ExcludedReason(@"web\lib\api.ts", linguistGenerated: false));
        Assert.Null(config.ExcludedReason("Web/lib/api.ts", linguistGenerated: false));
        Assert.Null(config.ExcludedReason("webapp/lib/api.ts", linguistGenerated: false));
        Assert.Equal("exclude:**/fixtures/**", config.ExcludedReason("fixtures/a.cs", linguistGenerated: false));
        Assert.Equal("exclude:**/fixtures/**", config.ExcludedReason("tests/fixtures/deep/a.cs", linguistGenerated: false));
        Assert.Equal("exclude:*.Gen.cs", config.ExcludedReason("src/Api/Quote.Gen.cs", linguistGenerated: false));
        Assert.Null(config.ExcludedReason("src/Api/Quote.gen.cs", linguistGenerated: false));
        Assert.Equal("exclude:src/?/*.sql", config.ExcludedReason("./src/a/init.sql", linguistGenerated: false));
        Assert.Null(config.ExcludedReason("src/ab/init.sql", linguistGenerated: false));
        Assert.True(config.ExcludedHere("web/package.json"));
        Assert.False(config.ExcludedHere("src/package.json"));
    }

    [Fact]
    public void WithExpressions_MatchTheNewGlobs_AndKeepValueEquality()
    {
        var config = Config.Default with { Exclude = ["web/**"] };
        Assert.Equal("exclude:web/**", config.ExcludedReason("web/a.ts", linguistGenerated: false));

        var changed = config with { Exclude = ["src/**"] };
        Assert.Null(changed.ExcludedReason("web/a.ts", linguistGenerated: false));
        Assert.Equal("exclude:src/**", changed.ExcludedReason("src/a.cs", linguistGenerated: false));
        Assert.False(changed.ExcludedHere("web/a.ts"));
        Assert.Equal(changed, changed with { Exclude = changed.Exclude });

        Assert.True(Tenancy.Applies("src/A.cs", Languages.CSharp));
        var web = Tenancy with { Globs = ["web/**"] };
        Assert.False(web.Applies("src/A.cs", Languages.CSharp));
        Assert.True(web.Applies("web/A.cs", Languages.CSharp));
        Assert.Equal(Tenancy, Tenancy with { Globs = Tenancy.Globs });
        Assert.NotEqual(Tenancy, web);

        var ux = new UserExperienceSettings { Enabled = true, Include = ["web/legacy/**"] };
        Assert.True(ux.Applies("web/legacy/render.js"));
        var narrowed = ux with { Include = [], Exclude = ["web/**"] };
        Assert.False(narrowed.Applies("web/legacy/render.js"));
        Assert.False(narrowed.Applies("web/Invoice.tsx"));
        Assert.True(ux.Applies("web/Invoice.tsx"));
        Assert.Equal(narrowed, narrowed with { Exclude = narrowed.Exclude });
    }

    [Fact]
    public void Json_KeepsLensAndUserExperienceFieldOrder()
    {
        var config = new Config([Tenancy])
        {
            Exclude = ["web/**"],
            UserExperience = new UserExperienceSettings { Enabled = true, Include = ["ui/**"], Exclude = ["ui/legacy/**"], BaseUrl = "http://localhost:3000" },
        };

        var json = ConfigJson.Serialize(config);
        var ux = JsonSerializer.Serialize(config.UserExperience, DomainJson.Options);

        Assert.Equal(
            ["\"id\"", "\"instructions\"", "\"globs\"", "\"languages\""],
            new[] { "\"id\"", "\"instructions\"", "\"globs\"", "\"languages\"" }.OrderBy(key => json.IndexOf(key, StringComparison.Ordinal)));
        Assert.Equal(
            """
            {
              "enabled": true,
              "include": [
                "ui/**"
              ],
              "exclude": [
                "ui/legacy/**"
              ],
              "base_url": "http://localhost:3000"
            }
            """.ReplaceLineEndings("\n"),
            ux);
        Assert.Contains("\"exclude\": [\n    \"web/**\"\n  ]", json);
    }

    [Theory]
    [InlineData("exclude")]
    [InlineData("lens")]
    [InlineData("ux")]
    public void Globs_AreCompiledOncePerConfig_NotOnEveryMatch(string holder)
    {
        string[] globs = ["tests/**", "demo/**", "src/lib/i18n/**", "mobile/**", "edge/**", "ui/**", "docs/**", "scripts/**", "tools/**", "samples/**"];
        var config = Config.Default with
        {
            Exclude = globs,
            Lenses = [new Lens("scoped", "Check.", globs, [])],
            UserExperience = new UserExperienceSettings { Enabled = true, Include = globs, Exclude = globs },
        };
        Func<string, bool> matches = holder switch
        {
            "exclude" => path => config.ExcludedReason(path, linguistGenerated: false) is not null,
            "lens" => path => config.Lenses[0].Applies(path, Languages.Python),
            _ => path => config.UserExperience.Applies(path),
        };
        var paths = Enumerable.Range(1, 30).SelectMany(folder => Enumerable.Range(1, 100).Select(file => $"pkg{folder}/m{file}.py")).ToArray();
        Assert.False(matches("warm/up.py"));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matched = paths.Count(matches);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        watch.Stop();

        Assert.Equal(0, matched);
        Assert.True(allocated < 32L * 1024 * 1024,
            $"matching {paths.Length} paths against {globs.Length} {holder} globs allocated {allocated / (1024 * 1024)} MB in {watch.ElapsedMilliseconds} ms; "
            + "each glob should be compiled once per config, not rebuilt as a regex on every match");
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
