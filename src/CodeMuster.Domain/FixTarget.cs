using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>A confirmed finding as a fix pack presents it: the id to answer with, the finding, and why verification kept it.</summary>
/// <param name="Id">The finding's ledger id.</param>
/// <param name="Finding">The finding itself (D11).</param>
/// <param name="ConfirmedBecause">The verifier's reason, when it recorded one.</param>
public sealed record FixTarget(long Id, Finding Finding, string? ConfirmedBecause);

/// <summary>Serializes what a fix pack shows.</summary>
public static class FixJson
{
    /// <summary>Writes the findings a fix unit must address, as indented JSON.</summary>
    public static string SerializeTargets(IReadOnlyList<FixTarget> targets) =>
        JsonSerializer.Serialize(targets, DomainJson.Options);
}
