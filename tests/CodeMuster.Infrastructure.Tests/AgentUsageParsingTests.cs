using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class AgentUsageParsingTests
{
    private const string Findings = """{"summary": "Nothing to report.", "findings": []}""";

    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "agent-output", name));

    private static ProcessOutput Exited(int code, string output, string error = "") => new(code, output, error);

    [Fact]
    public void Claude_real_call_returns_the_result_text_its_model_and_cost_with_the_one_hour_cache_writes()
    {
        var reply = ClaudeAdapter.Reply(Exited(0, Sample("claude-real.json")), "claude", []);

        Assert.Equal("OK", reply.Text);
        Assert.Equal(new AgentUsage(2, 4, 0, 45188, "claude-opus-5-5", 0.36159199999999997m) { CacheWrite1hTokens = 45188 }, reply.Usage);
    }

    [Fact]
    public void Claude_success_returns_the_result_text_verbatim_and_sums_every_model_it_used()
    {
        var reply = ClaudeAdapter.Reply(Exited(0, Sample("claude-documented-two-models.json")), "claude", []);

        Assert.Equal("```json\n" + Findings + "\n```", reply.Text);
        Assert.Equal(new AgentUsage(42 + 1830, 2210 + 95, 96120, 18234, "claude-opus-4-7", 0.4213m) { CacheWrite1hTokens = 0 }, reply.Usage);
    }

    [Fact]
    public void Claude_error_subtype_fails_the_call_with_its_errors_and_keeps_its_usage()
    {
        var error = Assert.Throws<AgentCallException>(() => ClaudeAdapter.Reply(Exited(1, Sample("claude-documented-error-max-turns.json")), "claude", ["--print"]));

        Assert.Contains("error_max_turns", error.Message);
        Assert.Contains("Reached maximum number of turns (10)", error.Message);
        Assert.Equal(new AgentUsage(88, 5120, 301442, 20110, "claude-opus-4-7", 0.8121m), error.Usage);
    }

    [Fact]
    public void Claude_api_error_reported_as_success_with_is_error_fails_the_call_even_on_exit_zero()
    {
        var error = Assert.Throws<AgentCallException>(() => ClaudeAdapter.Reply(Exited(0, Sample("claude-documented-api-error.json")), "claude", []));

        Assert.Contains("Invalid API key", error.Message);
        Assert.Equal(new AgentUsage(0, 0, 0, 0, null, 0m), error.Usage);
    }

    [Fact]
    public void Claude_output_that_is_not_json_is_taken_as_the_response_with_usage_unknown()
    {
        var reply = ClaudeAdapter.Reply(Exited(0, Findings + "\n"), "claude", []);

        Assert.Equal(new AgentReply(Findings + "\n", AgentUsage.Unknown), reply);
    }

    [Fact]
    public void Claude_nonzero_exit_without_json_fails_the_call_with_the_exit_code_and_stderr()
    {
        var error = Assert.Throws<AgentCallException>(() => ClaudeAdapter.Reply(Exited(2, "", "unknown option --bogus\n"), "/bin/claude", ["--bogus"]));

        Assert.Equal("claude --bogus exited with code 2: unknown option --bogus", error.Message);
        Assert.Equal(AgentUsage.Unknown, error.Usage);
    }

    [Fact]
    public void Claude_prints_json_with_read_only_tools_and_no_prompts()
    {
        string[] expected = ["--print", "--output-format", "json", "--permission-mode", "dontAsk", "--strict-mcp-config", "--no-session-persistence", "--tools", "Read,Glob,Grep"];

        Assert.Equal(expected, new ClaudeAdapter("claude").Arguments);
    }

    [Fact]
    public void Codex_real_call_splits_cached_input_out_of_input_and_names_no_model()
    {
        var reply = CodexAdapter.Reply(Exited(0, Sample("codex-real.jsonl"), Sample("codex-real.stderr.txt")), "OK", "codex", ["exec"]);

        Assert.Equal(new AgentReply("OK", new AgentUsage(25099 - 12800, 5, 12800, 0, null, null)), reply);
    }

    [Fact]
    public void Codex_usage_takes_the_rerouted_model()
    {
        Assert.Equal(new AgentUsage(24763 - 24448, 1122, 24448, 0, "gpt-5.4", null), CodexAdapter.Usage(Sample("codex-documented-reroute.jsonl")));
    }

    [Fact]
    public void Codex_failed_turn_reports_no_usage_and_fails_the_call()
    {
        var output = Sample("codex-documented-failed.jsonl");

        Assert.Equal(AgentUsage.Unknown, CodexAdapter.Usage(output));
        var error = Assert.Throws<AgentCallException>(() => CodexAdapter.Reply(Exited(1, output, "stream disconnected"), null, "codex", ["exec"]));
        Assert.Contains("exited with code 1", error.Message);
    }

    [Fact]
    public void Codex_reply_is_the_last_message_file_with_the_usage_of_the_event_stream()
    {
        var reply = CodexAdapter.Reply(Exited(0, Sample("codex-documented-reroute.jsonl")), Findings, "codex", ["exec"]);

        Assert.Equal(Findings, reply.Text);
        Assert.Equal(315, reply.Usage.InputTokens);
    }

    [Fact]
    public void Codex_without_a_last_message_fails_the_call_but_keeps_its_usage()
    {
        var error = Assert.Throws<AgentCallException>(() => CodexAdapter.Reply(Exited(0, Sample("codex-documented-reroute.jsonl")), null, "codex", ["exec"]));

        Assert.Equal(1122, error.Usage.OutputTokens);
    }

    [Fact]
    public void Codex_takes_the_last_running_total_when_several_turns_complete()
    {
        var output = """
            {"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":40,"output_tokens":10}}
            not json at all
            {"type":"turn.completed","usage":{"input_tokens":300,"cached_input_tokens":100,"output_tokens":30,"reasoning_output_tokens":5}}
            """;

        Assert.Equal(new AgentUsage(200, 30, 100, null, null, null), CodexAdapter.Usage(output));
    }

    [Fact]
    public void Codex_asks_for_json_events_and_still_writes_the_last_message()
    {
        var arguments = new CodexAdapter("codex").Arguments;

        Assert.Equal("--json", arguments[^4]);
        Assert.Equal("--output-last-message", arguments[^3]);
    }

    [Fact]
    public void Gemini_usage_moves_cached_prompt_tokens_out_of_input_and_bills_thoughts_as_output()
    {
        var output = Sample("gemini-documented-success.json");

        Assert.Equal(new AgentUsage(30645 + 900 - 28435, 812 + 1562 + 12, 28435, null, "gemini-2.5-pro", null), GeminiAdapter.Usage(output));
        Assert.Equal(Findings, GeminiAdapter.FinalText(output));
    }

    [Fact]
    public void Gemini_real_failure_names_the_error_from_stderr_past_the_warnings_with_usage_unknown()
    {
        var error = Assert.Throws<AgentCallException>(() => GeminiAdapter.Reply(Exited(41, "", Sample("gemini-real-error.stderr.txt")), "gemini", []));

        Assert.StartsWith("gemini reported an error (exit code 41): When using Gemini API, you must specify the GEMINI_API_KEY environment variable.", error.Message);
        Assert.Equal(AgentUsage.Unknown, error.Usage);
    }

    [Fact]
    public void Gemini_failure_without_a_json_error_reports_the_exit_code_and_stderr()
    {
        var error = Assert.Throws<AgentCallException>(() => GeminiAdapter.Reply(Exited(1, "", "boom\n"), "gemini", ["-m", "x"]));

        Assert.Equal("gemini -m x exited with code 1: boom", error.Message);
    }

    [Fact]
    public void OpenCode_real_call_reports_its_tokens_and_cost_and_no_model()
    {
        var reply = OpenCodeAdapter.Reply(Exited(0, Sample("opencode-real.jsonl")), "opencode", []);

        Assert.Equal(new AgentReply("OK", new AgentUsage(25610, 5, 0, 0, null, 0.05128m)), reply);
    }

    [Fact]
    public void OpenCode_usage_sums_every_step_with_reasoning_as_output_and_its_reported_cost()
    {
        var output = Sample("opencode-documented-two-steps.jsonl");

        Assert.Equal(new AgentUsage(800 + 1204, 120 + 80 + 410 + 256, 8000 + 19610, 0, null, 0.00512m + 0.01873m), OpenCodeAdapter.Usage(output));
        Assert.Equal(Findings, OpenCodeAdapter.Reply(Exited(0, output), "opencode", []).Text);
    }

    [Fact]
    public void OpenCode_zero_cost_for_a_model_it_cannot_price_is_not_taken_as_free()
    {
        var output = """{"type":"step_finish","timestamp":1,"sessionID":"s","part":{"type":"step-finish","cost":0,"tokens":{"input":10,"output":5,"reasoning":0,"cache":{"read":0,"write":0}}}}""";

        Assert.Equal(new AgentUsage(10, 5, 0, 0, null, null), OpenCodeAdapter.Usage(output));
    }

    [Fact]
    public void OpenCode_error_event_fails_the_call_but_keeps_the_usage_of_finished_steps()
    {
        var output = Sample("opencode-documented-two-steps.jsonl") + """{"type":"error","timestamp":2,"sessionID":"s","error":{"name":"APIError","data":{"message":"Rate limit exceeded"}}}""" + "\n";

        var error = Assert.Throws<AgentCallException>(() => OpenCodeAdapter.Reply(Exited(0, output), "opencode", []));

        Assert.Contains("Rate limit exceeded", error.Message);
        Assert.Equal(2004, error.Usage.InputTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"type\":\"step_finish\",\"part\":{\"tokens\":\"many\"}}")]
    [InlineData("{\"stats\":{\"models\":[]}}")]
    [InlineData("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":\"lots\"}}")]
    public void Malformed_usage_never_throws(string output)
    {
        Assert.Equal(AgentUsage.Unknown, CodexAdapter.Usage(output));
        Assert.Equal(AgentUsage.Unknown, GeminiAdapter.Usage(output));
        Assert.Equal(AgentUsage.Unknown, OpenCodeAdapter.Usage(output));
    }

    [Fact]
    public async Task Fake_agent_reports_a_small_fixed_usage_for_the_model_fake()
    {
        var adapter = new FakeAgentAdapter("""{"changes":{}}""", rawResponse: true);

        var reply = await adapter.RunAsync("pack", CancellationToken.None);

        Assert.Equal(new AgentUsage(1200, 300, 0, 0, "fake", null), reply.Usage);
    }
}
