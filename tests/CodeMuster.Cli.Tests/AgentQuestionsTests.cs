using CodeMuster.Infrastructure;

namespace CodeMuster.Cli.Tests;

public class AgentQuestionsTests
{
    private readonly StringWriter output = new();

    private AgentAnswers Ask(string typed, IReadOnlyList<string> installed, RememberedChoices? remembered = null, Dictionary<string, string>? given = null) =>
        new AgentQuestions(new StringReader(typed), output).Ask(installed, remembered, given ?? []);

    [Fact]
    public void BlankAnswers_TakeTheOnlyInstalledAgent_ProviderDefaults_AndFourJobs()
    {
        var answers = Ask("\n\n\n\n", ["claude"]);

        Assert.Equal(new AgentAnswers("claude", null, null, 4), answers);
        Assert.Contains("1) claude", output.ToString());
        Assert.Contains("Model [provider default]: ", output.ToString());
        Assert.Contains("Jobs [4]: ", output.ToString());
    }

    [Fact]
    public void AnAgentIsChosenByNumberOrName_AndTheOtherAnswersAreKept()
    {
        Assert.Equal(new AgentAnswers("codex", "gpt-5.5", "high", 8), Ask("2\ngpt-5.5\nhigh\n8\n", ["claude", "codex"]));
        Assert.Equal("codex", Ask("codex\n\n\n\n", ["claude", "codex"]).Agent);
    }

    [Fact]
    public void AnAnswerThatIsNotAChoice_IsAskedAgain()
    {
        var answers = Ask("7\nclaude\n\n\nmany\n0\n2\n", ["claude", "codex"]);

        Assert.Equal(new AgentAnswers("claude", null, null, 2), answers);
        Assert.Contains("choose 1-2 or an agent name", output.ToString());
        Assert.Contains("jobs must be a positive whole number", output.ToString());
    }

    [Fact]
    public void RememberedChoices_AreTheDefaults_WhileTheirAgentIsInstalled()
    {
        var remembered = new RememberedChoices("codex", "gpt-5.5", "high", 6, null);

        Assert.Equal(new AgentAnswers("codex", "gpt-5.5", "high", 6), Ask("\n\n\n\n", ["claude", "codex"], remembered));
        Assert.Contains("Model [gpt-5.5]: ", output.ToString());
        Assert.Equal(new AgentAnswers("claude", null, null, 6), Ask("\n\n\n\n", ["claude"], remembered));
    }

    [Fact]
    public void GivenOptions_SkipTheirQuestions()
    {
        var answers = Ask("\n\n", ["claude"], null, new() { ["jobs"] = "3", ["model"] = "claude-sonnet-5" });

        Assert.Equal(new AgentAnswers("claude", "claude-sonnet-5", null, 3), answers);
        Assert.DoesNotContain("Model [", output.ToString());
        Assert.DoesNotContain("Jobs [", output.ToString());
    }

    [Fact]
    public void Gemini_IsNotAskedForAThinkingLevel()
    {
        Ask("\n\n\n", ["gemini"]);

        Assert.DoesNotContain("Thinking [", output.ToString());
    }

    [Fact]
    public void NoInstalledAgent_SaysHowToInstallOne()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Ask("", []));

        Assert.Contains("no agent CLI was found on PATH", error.Message);
        Assert.Contains("npm install -g @anthropic-ai/claude-code", error.Message);
    }

    [Fact]
    public void TheEndOfInput_Cancels()
    {
        Assert.Throws<OperationCanceledException>(() => Ask("", ["claude"]));
    }
}
