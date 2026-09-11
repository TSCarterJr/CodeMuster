using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class InitTests
{
    private const string Root = "/repo";
    private static readonly string ConfigPath = ConfigLoader.PathFor(Root);
    private static readonly string GitignorePath = Init.GitignorePathFor(Root);

    private readonly FakeFileSystem fileSystem = new();
    private int confirmations;

    private Task<InitResult> RunAsync(bool confirm = true) =>
        new Init(fileSystem).RunAsync(Root, _ =>
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
        Assert.Equal(Config.Default.Lenses[0].Hash(), ConfigJson.Parse(fileSystem.Files[ConfigPath]).Lenses[0].Hash());
        Assert.EndsWith("\n", fileSystem.Files[ConfigPath]);
        Assert.Equal(".codemuster/ledger.db\n.codemuster/ledger.db-*\n", fileSystem.Files[GitignorePath]);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing_AndDoesNotAsk()
    {
        await RunAsync();
        var before = new Dictionary<string, string>(fileSystem.Files);

        var result = await RunAsync();

        Assert.False(result.ConfigCreated);
        Assert.Equal(GitignoreOutcome.AlreadyCovered, result.Gitignore);
        Assert.Equal(before, fileSystem.Files);
        Assert.Equal(1, confirmations);
    }

    [Theory]
    [InlineData("bin/\n.codemuster/ledger.db\n")]
    [InlineData("bin/\n/.codemuster/ledger.db\n")]
    [InlineData("  .codemuster/ledger.db  \r\nobj/\r\n")]
    public async Task GitignoreAlreadyCoveringLedger_IsLeftByteIdentical(string existing)
    {
        fileSystem.Files[GitignorePath] = existing;

        var result = await RunAsync();

        Assert.Equal(GitignoreOutcome.AlreadyCovered, result.Gitignore);
        Assert.Equal(existing, fileSystem.Files[GitignorePath]);
        Assert.Equal(0, confirmations);
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
