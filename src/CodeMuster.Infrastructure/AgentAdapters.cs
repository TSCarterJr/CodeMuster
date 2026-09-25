using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class AgentAdapters
{
    public static IAgentAdapter Create(string name, string? fakeTemplateJson, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) => name switch
    {
        "fake" => new FakeAgentAdapter(fakeTemplateJson ?? FakeAgentAdapter.DefaultTemplate, model, effort, workingDirectory: write ? workingDirectory : null),
        "claude" => new ClaudeAdapter(ExecutableResolver.Resolve("claude"), model, effort, write, workingDirectory),
        "codex" => new CodexAdapter(ExecutableResolver.Resolve("codex"), model, effort, write, workingDirectory),
        "gemini" => new GeminiAdapter(ExecutableResolver.Resolve("gemini"), model, effort, write, workingDirectory),
        "opencode" => new OpenCodeAdapter(ExecutableResolver.Resolve("opencode"), model, effort, write, workingDirectory),
        _ => throw new ArgumentException($"Unknown agent '{name}'. Valid names: fake, claude, codex, gemini, opencode.", nameof(name)),
    };
}
