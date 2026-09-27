using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeCommandRunner : ICommandRunner
{
    public HashSet<string> OnPath { get; } = new(StringComparer.Ordinal) { "git", "dotnet", "npm" };
    public Dictionary<string, CommandResult> Results { get; } = new(StringComparer.Ordinal)
    {
        ["dotnet --list-sdks"] = new(0, "10.0.100 [/usr/share/dotnet/sdk]\n", ""),
    };
    public List<(string WorkingDirectory, string Command)> Calls { get; } = [];

    public bool IsOnPath(string executable) => OnPath.Contains(executable);

    public Task<CommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var line = string.Join(' ', command);
        Calls.Add((workingDirectory, line));
        return Task.FromResult(Results.GetValueOrDefault(line, new CommandResult(0, "", "")));
    }
}
