namespace CodeMuster.Domain;

/// <summary>One event of the hidden engine stream (D65): its type and its fields, written in the order given. The stream adds the protocol version and the time. A field value is a string, a number, a bool, null, a list of those, or a nested field map.</summary>
/// <param name="Type">The event type, such as <c>unit_started</c>.</param>
/// <param name="Fields">The event's own fields, keyed by their JSON names.</param>
public sealed record EngineEvent(string Type, IReadOnlyDictionary<string, object?> Fields);

/// <summary>Receives the engine events of one command (D65). Workers may emit concurrently.</summary>
public interface IEngineEvents
{
    /// <summary>Appends one event.</summary>
    void Emit(EngineEvent engineEvent);
}
