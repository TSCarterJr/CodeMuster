using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>JSON contract for <see cref="VerifyResponse"/>: the sample a verify pack shows the model, plus parse and serialize.</summary>
public static class VerifyResponseJson
{
    /// <summary>The exact response text a verify pack asks the model to produce.</summary>
    public const string Sample = """
        {
          "verdict": "refuted",
          "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant."
        }
        """;

    /// <summary>Parses a model response; any malformed, missing, or unknown value surfaces as a <see cref="JsonException"/>.</summary>
    public static VerifyResponse Parse(string json) =>
        JsonSerializer.Deserialize<VerifyResponse>(json, DomainJson.Options)
        ?? throw new JsonException("response is null");

    /// <summary>Serializes a response in the same shape as <see cref="Sample"/>.</summary>
    public static string Serialize(VerifyResponse response) =>
        JsonSerializer.Serialize(response, DomainJson.Options);
}
