namespace CodeMuster.Application.Tests.Fakes;

public sealed class ListProgress : IProgress<string>
{
    public List<string> Messages { get; } = [];

    public void Report(string value) => Messages.Add(value);
}
