using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class RoslynMapperTests
{
    [Fact]
    public void Language_is_csharp()
    {
        Assert.Equal(Languages.CSharp, new RoslynMapper().Language);
    }

    [Fact]
    public void Opens_every_solution_when_any_is_listed()
    {
        Assert.Equal(
            new[] { "a/App.sln", "b/Tools.slnx" },
            RoslynMapper.WorkspaceFiles(["b/Tools.slnx", "src/App/App.csproj", "a/App.sln", "src/App/Program.cs"]));
    }

    [Fact]
    public void Opens_every_project_when_no_solution_is_listed()
    {
        Assert.Equal(
            new[] { "src/Api/Api.csproj", "src/Core/Core.csproj" },
            RoslynMapper.WorkspaceFiles(["src/Core/Core.csproj", "src/Api/Program.cs", "src/Api/Api.csproj"]));
    }

    [Fact]
    public async Task Throws_when_csharp_files_have_no_solution_or_project()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RoslynMapper().MapAsync(Path.GetTempPath(), ["src/Program.cs", "README.md"], CancellationToken.None));

        Assert.Equal("C# files found but no .sln, .slnx, or .csproj is included, so there is nothing to load them with", error.Message);
    }

    [Fact]
    public async Task Returns_an_empty_map_when_nothing_is_csharp()
    {
        var map = await new RoslynMapper().MapAsync(Path.GetTempPath(), ["README.md", "web/app.ts"], CancellationToken.None);

        Assert.Empty(map.Symbols);
        Assert.Empty(map.Edges);
        Assert.Empty(map.EntryPoints);
        Assert.Empty(map.Diagnostics);
    }

    [Fact]
    public async Task Loads_projects_when_no_solution_is_included()
    {
        using var copy = new FixtureCopy("minimal-api");
        var paths = Fixtures.IncludedPaths(copy.Root).Where(path => !path.EndsWith(".sln", StringComparison.Ordinal)).ToList();

        var map = await new RoslynMapper().MapAsync(copy.Root, paths, CancellationToken.None);

        var golden = Fixtures.Golden("minimal-api");
        Assert.Equal(golden.Symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal), map.Symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
        Assert.Contains("src/MinimalApi/MinimalApi.csproj is not restored; run dotnet restore src/MinimalApi/MinimalApi.csproj", map.Diagnostics);
    }

    [Fact]
    public async Task An_unrestored_project_leaves_package_calls_unresolved_and_names_the_restore_command()
    {
        using var copy = new FixtureCopy("mixed-repo");
        var project = copy.PathOf("src/MixedRepo.Api/MixedRepo.Api.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace(
            "</Project>",
            "  <ItemGroup>\n    <PackageReference Include=\"Humanizer.Core\" Version=\"2.14.1\" />\n  </ItemGroup>\n</Project>",
            StringComparison.Ordinal));
        File.WriteAllText(copy.PathOf("src/MixedRepo.Api/Shared/Words.cs"), """
            using Humanizer;

            namespace MixedRepo.Api.Shared;

            public static class Words
            {
                public static string Title(string text)
                {
                    return text.Humanize(LetterCasing.Title);
                }
            }
            """);

        var map = await new RoslynMapper().MapAsync(copy.Root, Fixtures.IncludedPaths(copy.Root), CancellationToken.None);

        Assert.Contains("src/MixedRepo.Api/MixedRepo.Api.csproj is not restored; run dotnet restore MixedRepo.sln", map.Diagnostics);
        Assert.Equal(21, map.Resolution.Resolved);
        Assert.Equal(1, map.Resolution.Unresolved);
        Assert.Equal(["Humanize"], map.Resolution.TopUnresolvedNames);
        Assert.Contains(map.Symbols, symbol => symbol.Id == "M:MixedRepo.Api.Shared.Words.Title(System.String)");
    }

    [Fact]
    public async Task Whitespace_only_edits_keep_body_hashes_and_code_edits_change_them()
    {
        using var copy = new FixtureCopy("mixed-repo");
        var service = copy.PathOf("src/MixedRepo.Api/Services/QuoteService.cs");
        var reformatted = File.ReadAllText(service).ReplaceLineEndings("\n")
            .Replace("    ", "\t", StringComparison.Ordinal)
            .Replace("{\n", "{\n\n", StringComparison.Ordinal)
            .Replace(", int id)", ",\n        int id)", StringComparison.Ordinal)
            .Replace(".Select(", "\n    .Select(", StringComparison.Ordinal)
            .ReplaceLineEndings("\r\n");
        File.WriteAllText(service, reformatted);
        var money = copy.PathOf("src/MixedRepo.Api/Shared/Money.cs");
        File.WriteAllText(money, File.ReadAllText(money).Replace("\"N2\"", "\"N0\"", StringComparison.Ordinal));

        var map = await new RoslynMapper().MapAsync(copy.Root, Fixtures.IncludedPaths(copy.Root), CancellationToken.None);

        var golden = Fixtures.Golden("mixed-repo").Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var edited = map.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var serviceIds = golden.Keys.Where(id => id.StartsWith("M:MixedRepo.Api.Services.QuoteService.", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, serviceIds.Count);
        Assert.All(serviceIds, id =>
        {
            Assert.NotEqual(golden[id].Range, edited[id].Range);
            Assert.Equal(golden[id].BodyHash, edited[id].BodyHash);
        });
        const string format = "M:MixedRepo.Api.Shared.Money.Format(System.Decimal)";
        Assert.NotEqual(golden[format].BodyHash, edited[format].BodyHash);
    }
}
