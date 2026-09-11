using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class AgentAdapters
{
    public static IAgentAdapter Create(string name, string? fakeTemplateJson) => name switch
    {
        "fake" => new FakeAgentAdapter(fakeTemplateJson ?? FakeAgentAdapter.DefaultTemplate),
        "claude" => new ClaudeAdapter(ExecutableResolver.Resolve("claude")),
        "codex" => new CodexAdapter(ExecutableResolver.Resolve("codex")),
        "gemini" => new GeminiAdapter(ExecutableResolver.Resolve("gemini")),
        "opencode" => new OpenCodeAdapter(ExecutableResolver.Resolve("opencode")),
        _ => throw new ArgumentException($"Unknown agent '{name}'. Valid names: fake, claude, codex, gemini, opencode.", nameof(name)),
    };
}
