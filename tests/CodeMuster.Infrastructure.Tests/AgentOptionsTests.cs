using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class AgentOptionsTests
{
    [Fact]
    public void Claude_takes_the_model_and_effort_it_was_given()
    {
        var adapter = new ClaudeAdapter("claude", "opus", "high");

        Assert.Equal(["--model", "opus"], Pair(adapter.Arguments, "--model"));
        Assert.Equal(["--effort", "high"], Pair(adapter.Arguments, "--effort"));
        Assert.Equal(new AgentIdentity("claude", "opus", "high"), adapter.Identity);
    }

    [Fact]
    public void Codex_passes_effort_as_a_config_override()
    {
        var adapter = new CodexAdapter("codex", "luna", "low");

        Assert.Equal(["-m", "luna"], Pair(adapter.Arguments, "-m"));
        Assert.Equal(["-c", "model_reasoning_effort=\"low\""], Pair(adapter.Arguments, "-c"));
        Assert.Equal(new AgentIdentity("codex", "luna", "low"), adapter.Identity);
    }

    [Fact]
    public void OpenCode_passes_effort_as_a_variant()
    {
        var adapter = new OpenCodeAdapter("opencode", "anthropic/claude-haiku-4-5", "high");

        Assert.Equal(["-m", "anthropic/claude-haiku-4-5"], Pair(adapter.Arguments, "-m"));
        Assert.Equal(["--variant", "high"], Pair(adapter.Arguments, "--variant"));
    }

    [Fact]
    public void Gemini_takes_a_model_but_says_it_has_no_effort_level()
    {
        Assert.Equal(["-m", "gemini-2.5-flash"], Pair(new GeminiAdapter("gemini", "gemini-2.5-flash", null).Arguments, "-m"));

        var error = Assert.Throws<ArgumentException>(() => new GeminiAdapter("gemini", null, "high"));

        Assert.Contains("gemini has no effort level", error.Message);
    }

    [Fact]
    public void WithoutModelOrEffort_NoFlagsAreAdded_AndTheIdentityIsJustTheName()
    {
        var adapter = new ClaudeAdapter("claude", null, null);

        Assert.DoesNotContain("--model", adapter.Arguments);
        Assert.DoesNotContain("--effort", adapter.Arguments);
        Assert.Equal(new AgentIdentity("claude", null, null), adapter.Identity);
        Assert.Equal(new AgentIdentity("fake", null, null), new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate).Identity);
    }

    private static IReadOnlyList<string> Pair(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        return index < 0 ? [] : [arguments[index], arguments[index + 1]];
    }
}
