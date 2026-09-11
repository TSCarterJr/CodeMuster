using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class ClaudeAdapter(string executable) : IAgentAdapter
{
    public IReadOnlyList<string> Arguments { get; } =
        ["--print", "--output-format", "text", "--permission-mode", "dontAsk", "--strict-mcp-config", "--no-session-persistence", "--tools", "Read,Glob,Grep"];

    public Task<string> RunAsync(string pack, CancellationToken cancellationToken) =>
        HeadlessProcess.RunAsync(executable, Arguments, pack, cancellationToken);
}
