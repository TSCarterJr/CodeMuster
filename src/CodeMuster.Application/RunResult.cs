namespace CodeMuster.Application;

/// <summary>How a <see cref="Run"/> ended.</summary>
/// <param name="Completed">Units recorded this run.</param>
/// <param name="GaveUp">Units that used every attempt without being recorded, in the order the run gave up on them.</param>
/// <param name="Cancelled">True when the run stopped because its token was cancelled; in-flight units were left as they were.</param>
public sealed record RunResult(int Completed, IReadOnlyList<string> GaveUp, bool Cancelled);
