using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class AgentAdapters
{
    // The test-only fake agent is accepted but never offered to users.
    public static readonly IReadOnlyList<string> Names = ["claude", "codex", "gemini", "opencode"];

    public static IAgentAdapter Create(string name, string? fakeTemplateJson, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) => name switch
    {
        "fake" => new FakeAgentAdapter(fakeTemplateJson ?? FakeAgentAdapter.DefaultTemplate, model, effort, workingDirectory: write ? workingDirectory : null),
        "claude" => new ClaudeAdapter(ExecutableResolver.Resolve("claude"), model, effort, write, workingDirectory),
        "codex" => new CodexAdapter(ExecutableResolver.Resolve("codex"), model, effort, write, workingDirectory),
        "gemini" => new GeminiAdapter(ExecutableResolver.Resolve("gemini"), model, effort, write, workingDirectory),
        "opencode" => new OpenCodeAdapter(ExecutableResolver.Resolve("opencode"), model, effort, write, workingDirectory),
        _ => throw new ArgumentException($"unknown agent '{name}'; choose one of {string.Join(", ", Names)}"),
    };
}
