using CodeMuster.Domain;

namespace CodeMuster.Application;

// Builds the events of the hidden engine stream (D65); docs/internal/engine-protocol.md lists every type and field.
internal static class EngineStream
{
    public static void Emit(this IEngineEvents? events, string type, params (string Name, object? Value)[] fields)
    {
        if (events is null) return;
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in fields) map[name] = value;
        events.Emit(new EngineEvent(type, map));
    }

    public static void Started(this IEngineEvents? events, string command, int workers, AgentIdentity identity) =>
        events.Emit("run_started", ("command", command), ("workers", workers), ("agent", identity.Agent), ("model", identity.Model), ("effort", identity.Effort));

    public static void UnitStarted(this IEngineEvents? events, string unitId, UnitKind kind, string key, int worker, int attempt) =>
        events.Emit("unit_started", ("unit", unitId), ("kind", Name(kind)), ("key", key), ("worker", worker), ("attempt", attempt));

    public static void UnitFinished(this IEngineEvents? events, string unitId, UnitKind kind, string key, int attempt, string outcome, string message, bool gaveUp, long durationMs, AgentCall? call) =>
        events.Emit("unit_finished", ("unit", unitId), ("kind", Name(kind)), ("key", key), ("attempt", attempt), ("outcome", outcome), ("message", message),
            ("gave_up", gaveUp), ("duration_ms", durationMs), ("usage", call is null ? null : Usage(call.Usage)), ("cost_usd", call?.CostUsd), ("cost_source", call?.CostSource));

    public static void Skipped(this IEngineEvents? events, string unitId, UnitKind kind, string key, string reason) =>
        events.Emit("skipped", ("unit", unitId), ("kind", Name(kind)), ("key", key), ("reason", reason));

    /// <summary>Reports every progress line as a scan_progress event too, or returns the progress unchanged when there is no stream.</summary>
    public static IProgress<string>? Tee(IProgress<string>? progress, IEngineEvents? events) =>
        events is null ? progress : new TeeProgress(progress, events);

    public static string Name(UnitKind kind) => kind.ToString().ToLowerInvariant();

    public static string Name(DoneOutcome outcome) => outcome == DoneOutcome.InvalidResponse ? "invalid_response" : outcome.ToString().ToLowerInvariant();

    public static long Milliseconds(DateTimeOffset from, DateTimeOffset to) => (long)(to - from).TotalMilliseconds;

    // Lowest slot number not in use, so a dashboard can draw one lane per worker.
    public static int FreeWorker(IEnumerable<int> busy)
    {
        var taken = busy.ToHashSet();
        var worker = 1;
        while (taken.Contains(worker)) worker++;
        return worker;
    }

    private static Dictionary<string, object?> Usage(AgentUsage usage) => new(StringComparer.Ordinal)
    {
        ["input_tokens"] = usage.InputTokens,
        ["output_tokens"] = usage.OutputTokens,
        ["cache_read_tokens"] = usage.CacheReadTokens,
        ["cache_write_tokens"] = usage.CacheWriteTokens,
        ["model"] = usage.Model,
        ["reported_cost_usd"] = usage.ReportedCostUsd,
    };

    private sealed class TeeProgress(IProgress<string>? inner, IEngineEvents events) : IProgress<string>
    {
        public void Report(string value)
        {
            inner?.Report(value);
            events.Emit("scan_progress", ("message", value));
        }
    }
}
