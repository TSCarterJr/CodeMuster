using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>The JSON contract for a fix unit's reply (D37).</summary>
public static class FixResponseJson
{
    /// <summary>The shape the pack asks for, and the text the skill shows.</summary>
    public static string Sample { get; } = Serialize(new FixResponse(
        "Scoped the query to the caller's tenant and awaited the archive call.",
        [18],
        [new DeclinedFix(19, "The reported race cannot happen: the method is called once, from a single-threaded startup path.")]));

    /// <summary>Parses a reply, throwing <see cref="JsonException"/> when a required field is missing.</summary>
    public static FixResponse Parse(string json) =>
        JsonSerializer.Deserialize<FixResponse>(json, DomainJson.Options) ?? throw new JsonException("fix response is null");

    /// <summary>Writes a reply as indented JSON.</summary>
    public static string Serialize(FixResponse response) => JsonSerializer.Serialize(response, DomainJson.Options);
}
