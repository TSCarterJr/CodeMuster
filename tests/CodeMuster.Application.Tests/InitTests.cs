using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class InitTests
{
    private const string Root = "/repo";
    private static readonly string ConfigPath = ConfigLoader.PathFor(Root);
    private static readonly string GitignorePath = Init.GitignorePathFor(Root);

    private readonly FakeFileSystem fileSystem = new();
    private readonly FakeSourceTree tree = new();
    private int confirmations;

    private Task<InitResult> RunAsync(bool confirm = true) =>
        new Init(fileSystem, tree).RunAsync(Root, _ =>
        {
            confirmations++;
            return Task.FromResult(confirm);
        }, CancellationToken.None);

    [Fact]
    public async Task EmptyRepo_CreatesDefaultConfig_AndGitignoreWithLedgerLines()
    {
        var result = await RunAsync();

        Assert.True(result.ConfigCreated);
        Assert.Equal(GitignoreOutcome.Appended, result.Gitignore);
        Assert.Equal(ConfigJson.Serialize(Config.Default) + "\n", fileSystem.Files[ConfigPath]);
        Assert.Equal(".codemuster/ledger.db\n.codemuster/ledger.db-*\n", fileSystem.Files[GitignorePath]);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing_AndDoesNotAsk()
    {
        await RunAsync();
        tree.Ignored.Add(Init.LedgerPath);
        var before = new Dictionary<string, string>(fileSystem.Files);

        var result = await RunAsync();

        Assert.False(result.ConfigCreated);
        Assert.Equal(GitignoreOutcome.AlreadyCovered, result.Gitignore);
        Assert.Equal(before, fileSystem.Files);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task LedgerAlreadyIgnoredByGit_LeavesGitignoreByteIdentical_AndDoesNotAsk()
    {
        fileSystem.Files[GitignorePath] = "bin/\r\n.codemuster/\r\n";
        tree.Ignored.Add(Init.LedgerPath);

        var result = await RunAsync();

        Assert.Equal(GitignoreOutcome.AlreadyCovered, result.Gitignore);
        Assert.Equal("bin/\r\n.codemuster/\r\n", fileSystem.Files[GitignorePath]);
        Assert.Equal(0, confirmations);
    }

    [Fact]
    public async Task GitignoreTextThatGitDoesNotHonour_StillGetsTheLines()
    {
        fileSystem.Files[GitignorePath] = "  .codemuster/ledger.db\n";

        var result = await RunAsync();

        Assert.Equal(GitignoreOutcome.Appended, result.Gitignore);
        Assert.Equal("  .codemuster/ledger.db\n.codemuster/ledger.db\n.codemuster/ledger.db-*\n", fileSystem.Files[GitignorePath]);
    }

    [Fact]
    public async Task ExistingGitignoreWithoutTrailingNewline_GetsLinesOnTheirOwnLines()
    {
        fileSystem.Files[GitignorePath] = "bin/\nobj/";

        await RunAsync();

        Assert.Equal("bin/\nobj/\n.codemuster/ledger.db\n.codemuster/ledger.db-*\n", fileSystem.Files[GitignorePath]);
    }

    [Fact]
    public async Task Declined_LeavesGitignoreAlone_ButStillCreatesConfig()
    {
        fileSystem.Files[GitignorePath] = "bin/\n";

        var result = await RunAsync(confirm: false);

        Assert.True(result.ConfigCreated);
        Assert.Equal(GitignoreOutcome.Skipped, result.Gitignore);
        Assert.Equal("bin/\n", fileSystem.Files[GitignorePath]);
        Assert.True(fileSystem.FileExists(ConfigPath));
    }

    [Fact]
    public async Task ExistingConfig_IsNeverOverwritten()
    {
        fileSystem.Files[ConfigPath] = "{ \"lenses\": [] }";

        var result = await RunAsync();

        Assert.False(result.ConfigCreated);
        Assert.Equal("{ \"lenses\": [] }", fileSystem.Files[ConfigPath]);
    }
}
