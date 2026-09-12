namespace CodeMuster.Application.Tests.Fakes;

public sealed class ListProgress(List<string>? messages = null) : IProgress<string>
{
    public List<string> Messages { get; } = messages ?? [];

    public void Report(string value) => Messages.Add(value);
}
