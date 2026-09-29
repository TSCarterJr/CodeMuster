using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>JSON contract for <see cref="VerifyBatchResponse"/>: the sample a batched verify pack shows the model, plus parse and serialize (D78).</summary>
public static class VerifyBatchResponseJson
{
    /// <summary>The exact response text a batched verify pack asks the model to produce.</summary>
    public const string Sample = """
        {
          "verdicts": [
            { "finding": 12, "verdict": "confirmed", "reason": "Line 42 reads Status before the tenant filter on line 44 runs, so another tenant's rows are returned." },
            { "finding": 13, "verdict": "refuted", "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant." }
          ]
        }
        """;

    /// <summary>Parses a model response; any malformed, missing, or unknown value surfaces as a <see cref="JsonException"/>.</summary>
    public static VerifyBatchResponse Parse(string json) =>
        JsonSerializer.Deserialize<VerifyBatchResponse>(json, DomainJson.Options)
        ?? throw new JsonException("response is null");

    /// <summary>Serializes a response in the same shape as <see cref="Sample"/>.</summary>
    public static string Serialize(VerifyBatchResponse response) =>
        JsonSerializer.Serialize(response, DomainJson.Options);
}
