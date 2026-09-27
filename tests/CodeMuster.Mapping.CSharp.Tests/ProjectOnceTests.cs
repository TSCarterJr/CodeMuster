namespace CodeMuster.Mapping.CSharp.Tests;

public class ProjectOnceTests
{
    [Theory]
    [InlineData("MixedRepo.slnx", """
        <Solution>
          <Folder Name="/src/">
            <Project Path="src\MixedRepo.Api\MixedRepo.Api.csproj" />
          </Folder>
        </Solution>
        """)]
    [InlineData("Tools.sln", null)]
    public async Task A_second_solution_over_loaded_projects_is_skipped(string solution, string? content)
    {
        using var copy = new FixtureCopy("mixed-repo");
        File.WriteAllText(copy.PathOf(solution), content ?? File.ReadAllText(copy.PathOf("MixedRepo.sln")));
        var progress = new ListProgress();

        var map = await new RoslynMapper().MapAsync(copy.Root, Fixtures.IncludedPaths(copy.Root), progress, CancellationToken.None);

        var golden = Fixtures.Golden("mixed-repo");
        Assert.Equal(golden.Resolution.Resolved, map.Resolution.Resolved);
        Assert.Equal(golden.Resolution.Unresolved, map.Resolution.Unresolved);
        Assert.Equal(golden.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal), map.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal));
        Assert.Single(progress.Messages, message => message.StartsWith("reading MixedRepo.Api,", StringComparison.Ordinal));
        Assert.DoesNotContain($"loading {solution}", progress.Messages);
        Assert.Contains($"skipping {solution}, its projects are already loaded", progress.Messages);
    }

    [Fact]
    public async Task A_project_outside_the_solution_does_not_map_the_solution_projects_it_references_again()
    {
        using var copy = new FixtureCopy("mixed-repo");
        Directory.CreateDirectory(copy.PathOf("tools/Extra"));
        File.WriteAllText(copy.PathOf("tools/Extra/Extra.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../../src/MixedRepo.Api/MixedRepo.Api.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(copy.PathOf("tools/Extra/Labels.cs"), """
            namespace Extra;

            public static class Labels
            {
                public static string Price(decimal amount)
                {
                    return MixedRepo.Api.Shared.Money.Format(amount);
                }
            }
            """);
        var progress = new ListProgress();

        var map = await new RoslynMapper().MapAsync(copy.Root, Fixtures.IncludedPaths(copy.Root), progress, CancellationToken.None);

        var golden = Fixtures.Golden("mixed-repo");
        Assert.Equal(golden.Resolution.Resolved + 1, map.Resolution.Resolved);
        Assert.Equal(golden.Resolution.Unresolved, map.Resolution.Unresolved);
        Assert.Single(progress.Messages, message => message.StartsWith("reading MixedRepo.Api,", StringComparison.Ordinal));
        Assert.Contains("reading Extra, 1 files", progress.Messages);
        Assert.Contains(map.Edges, edge => edge.From == "M:Extra.Labels.Price(System.Decimal)" && edge.To == "M:MixedRepo.Api.Shared.Money.Format(System.Decimal)");
    }

    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value) => Messages.Add(value);
    }
}
