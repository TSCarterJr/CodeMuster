using CodeMuster.Domain;

namespace CodeMuster.Application.Tests.Fakes;

public sealed class RecordingEvents : IEngineEvents
{
    private readonly List<EngineEvent> events = [];

    public IReadOnlyList<EngineEvent> Events
    {
        get
        {
            lock (events) return events.ToList();
        }
    }

    public IReadOnlyList<EngineEvent> OfType(string type) => Events.Where(e => e.Type == type).ToList();

    public void Emit(EngineEvent engineEvent)
    {
        lock (events) events.Add(engineEvent);
    }
}
