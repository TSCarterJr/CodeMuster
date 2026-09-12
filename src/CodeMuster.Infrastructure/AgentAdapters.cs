using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class AgentAdapters
{
    public static IAgentAdapter Create(string name, string? fakeTemplateJson, string? model = null, string? effort = null, bool write = false) => name switch
    {
        "fake" => new FakeAgentAdapter(fakeTemplateJson ?? FakeAgentAdapter.DefaultTemplate, model, effort),
        "claude" => new ClaudeAdapter(ExecutableResolver.Resolve("claude"), model, effort, write),
        "codex" => new CodexAdapter(ExecutableResolver.Resolve("codex"), model, effort, write),
        "gemini" => new GeminiAdapter(ExecutableResolver.Resolve("gemini"), model, effort, write),
        "opencode" => new OpenCodeAdapter(ExecutableResolver.Resolve("opencode"), model, effort, write),
        _ => throw new ArgumentException($"Unknown agent '{name}'. Valid names: fake, claude, codex, gemini, opencode.", nameof(name)),
    };
}
