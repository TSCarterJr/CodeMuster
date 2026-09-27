using System.Threading.Channels;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class FakeControl : IEngineControl
{
    private readonly Channel<string> lines = Channel.CreateUnbounded<string>();

    public void Send(string line) => lines.Writer.TryWrite(line);

    public async Task<string> NextCommandAsync(CancellationToken cancellationToken) => await lines.Reader.ReadAsync(cancellationToken);
}
