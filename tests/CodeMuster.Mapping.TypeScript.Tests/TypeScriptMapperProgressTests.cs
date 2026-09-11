namespace CodeMuster.Mapping.TypeScript.Tests;

public class TypeScriptMapperProgressTests
{
    [Fact]
    public async Task Reports_each_tsconfig_and_the_files_it_maps()
    {
        var root = TestPaths.MixedRepoWithTypeScript();
        var progress = new ListProgress();

        await new TypeScriptMapper().MapAsync(root, TestPaths.RepoPaths(root), progress, CancellationToken.None);

        Assert.Equal("loading web/tsconfig.json", progress.Messages[0]);
        Assert.Matches(@"^mapped (\d+)/\1 files$", progress.Messages[^1]);
    }

    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value) => Messages.Add(value);
    }
}
