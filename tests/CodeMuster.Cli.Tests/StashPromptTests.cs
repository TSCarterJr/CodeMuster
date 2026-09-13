namespace CodeMuster.Cli.Tests;

public class StashPromptTests
{
    [Theory]
    [InlineData("y", true)]
    [InlineData("YES", true)]
    [InlineData("  yes  ", true)]
    [InlineData("", false)]
    [InlineData("no", false)]
    [InlineData("yesterday", false)]
    [InlineData(null, false)]
    public void Terminal_RequiresAnExplicitYes(string? answer, bool expected)
    {
        Assert.Equal(expected, StashPrompt.Decide(new HashSet<string>(), true, () => answer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StashFlag_AuthorizesWithoutAsking(bool interactive)
    {
        Assert.True(StashPrompt.Decide(new HashSet<string> { "stash" }, interactive, () => throw new Exception("must not prompt")));
    }

    [Fact]
    public void Noninteractive_WithoutTheFlag_DoesNotStashOrAsk()
    {
        Assert.False(StashPrompt.Decide(new HashSet<string>(), false, () => throw new Exception("must not prompt")));
    }
}
