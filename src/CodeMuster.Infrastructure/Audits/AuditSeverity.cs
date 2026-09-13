using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Audits;

internal static class AuditSeverity
{
    public static Severity Parse(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => Severity.Critical,
        "high" => Severity.High,
        "moderate" or "medium" => Severity.Medium,
        "low" => Severity.Low,
        _ => Severity.Info,
    };
}
