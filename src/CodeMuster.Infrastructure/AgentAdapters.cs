using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class AgentAdapters
{
    public static IAgentAdapter Create(string name, string? fakeTemplateJson, string? model = null, string? effort = null) => name switch
    {
        "fake" => new FakeAgentAdapter(fakeTemplateJson ?? FakeAgentAdapter.DefaultTemplate, model, effort),
        "claude" => new ClaudeAdapter(ExecutableResolver.Resolve("claude"), model, effort),
        "codex" => new CodexAdapter(ExecutableResolver.Resolve("codex"), model, effort),
        "gemini" => new GeminiAdapter(ExecutableResolver.Resolve("gemini"), model, effort),
        "opencode" => new OpenCodeAdapter(ExecutableResolver.Resolve("opencode"), model, effort),
        _ => throw new ArgumentException($"Unknown agent '{name}'. Valid names: fake, claude, codex, gemini, opencode.", nameof(name)),
    };
}
