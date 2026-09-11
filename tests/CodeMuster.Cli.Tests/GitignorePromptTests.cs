namespace CodeMuster.Cli.Tests;

public class GitignorePromptTests
{
    private int asked;

    private bool Decide(bool interactive, string? answer, params string[] flags) =>
        GitignorePrompt.Decide(flags.ToHashSet(StringComparer.Ordinal), interactive, () =>
        {
            asked++;
            return answer;
        });

    [Fact]
    public void NoGitignore_WinsOverYes_WithoutAsking()
    {
        Assert.False(Decide(interactive: true, "y", "yes", "no-gitignore"));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Yes_AppendsWithoutAsking_EvenOnATerminal()
    {
        Assert.True(Decide(interactive: true, "n", "yes"));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void NoTerminal_AppendsWithoutAsking()
    {
        Assert.True(Decide(interactive: false, "n"));
        Assert.Equal(0, asked);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("y", true)]
    [InlineData("Yes", true)]
    [InlineData("  y  ", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("maybe", false)]
    [InlineData(null, false)]
    public void Terminal_AsksOnce_AndDefaultsToYes(string? answer, bool expected)
    {
        Assert.Equal(expected, Decide(interactive: true, answer));
        Assert.Equal(1, asked);
    }
}
