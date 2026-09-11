namespace CodeMuster.Mapping.CSharp.Tests;

public class RoslynMapperProgressTests
{
    [Fact]
    public async Task Reports_the_solution_each_project_the_bindings_and_the_files_it_maps()
    {
        using var copy = new FixtureCopy("mixed-repo");
        var progress = new ListProgress();

        await new RoslynMapper().MapAsync(copy.Root, Fixtures.IncludedPaths(copy.Root), progress, CancellationToken.None);

        Assert.Equal("loading MixedRepo.sln", progress.Messages[0]);
        Assert.Contains(progress.Messages, message => message.StartsWith("loaded src/MixedRepo.Api/MixedRepo.Api.csproj in ", StringComparison.Ordinal));
        Assert.Contains("finding dependency injection bindings", progress.Messages);
        Assert.Contains(progress.Messages, message => System.Text.RegularExpressions.Regex.IsMatch(message, @"^reading MixedRepo\.Api, \d+ files$"));
        Assert.Matches(@"^mapped (\d+)/\1 files$", progress.Messages[^1]);
    }

    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value) => Messages.Add(value);
    }
}
