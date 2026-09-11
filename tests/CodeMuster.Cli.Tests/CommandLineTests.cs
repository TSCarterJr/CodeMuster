namespace CodeMuster.Cli.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_VerbWithOptionsAndPositional()
    {
        var command = CommandLine.Parse(["done", "file:src/A.cs", "--fingerprint", "abc", "--findings", "f.json"]);

        Assert.NotNull(command);
        Assert.Equal("done", command.Verb);
        Assert.Equal(["file:src/A.cs"], command.Positionals);
        Assert.Equal("abc", command.Options["fingerprint"]);
        Assert.Equal("f.json", command.Options["findings"]);
    }

    [Fact]
    public void Parse_NoArguments_ReturnsNull()
    {
        Assert.Null(CommandLine.Parse([]));
    }

    [Theory]
    [InlineData("--batch")]
    [InlineData("--batch --out x")]
    [InlineData("--out x --batch")]
    public void Parse_OptionWithoutValue_ReturnsNull(string tail)
    {
        Assert.Null(CommandLine.Parse(["next", .. tail.Split(' ')]));
    }

    [Fact]
    public void Parse_KnownFlags_TakeNoValue()
    {
        var command = CommandLine.Parse(["init", "--yes", "--no-gitignore"]);

        Assert.NotNull(command);
        Assert.Equal(["no-gitignore", "yes"], command.Flags.Order());
        Assert.Empty(command.Options);
        Assert.Empty(CommandLine.Parse(["init"])!.Flags);
    }

    [Fact]
    public void Parse_ShortJobsOption_AndRunFlags()
    {
        var command = CommandLine.Parse(["run", "--agent", "fake", "-j", "4", "--force"]);

        Assert.NotNull(command);
        Assert.Equal("4", command.Options["jobs"]);
        Assert.Equal("fake", command.Options["agent"]);
        Assert.Contains("force", command.Flags);
        Assert.Empty(command.Positionals);
        Assert.Null(CommandLine.Parse(["run", "-j"]));
    }

    [Fact]
    public void Parse_LaterOptionWins_AndVerbIsLowercased()
    {
        var command = CommandLine.Parse(["NEXT", "--batch", "1", "--batch", "3"]);

        Assert.NotNull(command);
        Assert.Equal("next", command.Verb);
        Assert.Equal("3", command.Options["batch"]);
    }
}
