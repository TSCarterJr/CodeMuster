namespace CodeMuster.Infrastructure.Tests;

public class HarnessAdapterTests
{
    [Fact]
    public void Claude_prints_text_with_read_only_tools_and_no_prompts()
    {
        string[] expected = ["--print", "--output-format", "text", "--permission-mode", "dontAsk", "--strict-mcp-config", "--no-session-persistence", "--tools", "Read,Glob,Grep"];

        Assert.Equal(expected, new ClaudeAdapter("claude").Arguments);
    }

    [Fact]
    public void Codex_execs_read_only_from_stdin_and_writes_the_last_message_to_a_temp_file()
    {
        var arguments = new CodexAdapter("codex").Arguments;
        string[] expected = ["exec", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral", "--color", "never", "--output-last-message"];

        Assert.Equal(expected, arguments.Take(expected.Length));
        Assert.Equal(expected.Length + 2, arguments.Count);
        Assert.StartsWith(Path.GetTempPath(), arguments[expected.Length]);
        Assert.EndsWith(".md", arguments[expected.Length]);
        Assert.Equal("-", arguments[^1]);
    }

    [Fact]
    public void Codex_uses_a_fresh_last_message_file_per_launch()
    {
        var adapter = new CodexAdapter("codex");

        Assert.NotEqual(adapter.Arguments[^2], adapter.Arguments[^2]);
    }

    [Fact]
    public void Gemini_prints_json_under_the_default_approval_mode()
    {
        string[] expected = ["--output-format", "json", "--approval-mode", "default"];

        Assert.Equal(expected, new GeminiAdapter("gemini").Arguments);
    }

    [Fact]
    public void OpenCode_runs_the_plan_agent_with_json_events()
    {
        string[] expected = ["run", "--agent", "plan", "--format", "json"];

        Assert.Equal(expected, new OpenCodeAdapter("opencode").Arguments);
    }

    [Fact]
    public void Gemini_final_text_is_the_response_field_of_the_json_envelope()
    {
        var output = "Loaded cached credentials.\n{\n  \"session_id\": \"abc\",\n  \"response\": \"{\\\"summary\\\":\\\"s\\\",\\\"findings\\\":[]}\",\n  \"stats\": {}\n}\n";

        Assert.Equal("{\"summary\":\"s\",\"findings\":[]}", GeminiAdapter.FinalText(output));
    }

    [Fact]
    public void Gemini_output_without_a_response_throws_with_the_output()
    {
        var output = "{\"session_id\":\"abc\",\"error\":{\"type\":\"FatalAuthenticationError\",\"message\":\"no auth\"}}\n";

        var ex = Assert.Throws<InvalidOperationException>(() => GeminiAdapter.FinalText(output));

        Assert.Contains("no auth", ex.Message);
    }

    [Fact]
    public void Gemini_output_without_json_throws()
    {
        Assert.Throws<InvalidOperationException>(() => GeminiAdapter.FinalText("nothing here\n"));
    }

    [Fact]
    public void OpenCode_final_text_is_the_last_text_event_and_skips_noise()
    {
        var output = string.Join("\r\n", [
            "{\"type\":\"step_start\",\"timestamp\":1,\"sessionID\":\"s\",\"part\":{}}",
            "{\"type\":\"text\",\"timestamp\":2,\"sessionID\":\"s\",\"part\":{\"type\":\"text\",\"text\":\"Let me look.\"}}",
            "! permission requested: edit; auto-rejecting",
            "{\"type\":\"tool_use\",\"timestamp\":3,\"sessionID\":\"s\",\"part\":{}}",
            "{\"type\":\"text\",\"timestamp\":4,\"sessionID\":\"s\",\"part\":{\"type\":\"text\",\"text\":\"{\\\"summary\\\":\\\"s\\\"}\"}}",
            "{\"type\":\"step_finish\",\"timestamp\":5,\"sessionID\":\"s\",\"part\":{}}",
            ""]);

        Assert.Equal("{\"summary\":\"s\"}", OpenCodeAdapter.FinalText(output));
    }

    [Fact]
    public void OpenCode_error_event_throws_with_its_message()
    {
        var output = "{\"type\":\"error\",\"timestamp\":1,\"sessionID\":\"s\",\"error\":{\"name\":\"ProviderAuthError\",\"data\":{\"message\":\"no key\"}}}\n";

        var ex = Assert.Throws<InvalidOperationException>(() => OpenCodeAdapter.FinalText(output));

        Assert.Contains("no key", ex.Message);
    }

    [Fact]
    public void OpenCode_output_without_text_throws()
    {
        Assert.Throws<InvalidOperationException>(() => OpenCodeAdapter.FinalText("{\"type\":\"step_finish\",\"timestamp\":1,\"sessionID\":\"s\",\"part\":{}}\n"));
    }
}
