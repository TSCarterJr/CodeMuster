using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class ClaudeAdapter(string executable, string? model = null, string? effort = null, bool write = false, string? workingDirectory = null) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } =
    [
        "--print", "--output-format", "text",
        "--permission-mode", write ? "acceptEdits" : "dontAsk",
        "--strict-mcp-config", "--no-session-persistence",
        "--tools", write ? "Read,Glob,Grep,Edit,Write" : "Read,Glob,Grep",
        .. model is null ? Array.Empty<string>() : ["--model", model],
        .. effort is null ? Array.Empty<string>() : ["--effort", effort],
    ];

    public AgentIdentity Identity { get; } = new("claude", model, effort);

    public Task<string> RunAsync(string pack, CancellationToken cancellationToken) =>
        HeadlessProcess.RunAsync(executable, Arguments, pack, cancellationToken, workingDirectory);
}
