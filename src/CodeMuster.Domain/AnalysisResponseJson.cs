using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>JSON contract for <see cref="AnalysisResponse"/>: the sample the skill shows the model, plus parse and serialize.</summary>
public static class AnalysisResponseJson
{
    /// <summary>The exact response text the skill asks the model to produce.</summary>
    public const string Sample = """
        {
          "summary": "Repository for quotes; every query is expected to be scoped to the caller's tenant.",
          "findings": [
            {
              "path": "src/MixedRepo.Api/Data/QuoteRepository.cs",
              "line_start": 18,
              "line_end": 21,
              "severity": "high",
              "category": "security",
              "claim": "ListForTenant returns quotes for every tenant because the query has no TenantId filter.",
              "evidence": "The Where clause filters on Status only; TenantId is never referenced.",
              "confidence": 0.9,
              "lens_id": "default"
            }
          ]
        }
        """;

    /// <summary>Parses a model response; any malformed, missing, or unknown value surfaces as a <see cref="JsonException"/>.</summary>
    public static AnalysisResponse Parse(string json) =>
        JsonSerializer.Deserialize<AnalysisResponse>(json, DomainJson.Options)
        ?? throw new JsonException("response is null");

    /// <summary>Serializes a response in the same shape as <see cref="Sample"/>.</summary>
    public static string Serialize(AnalysisResponse response) =>
        JsonSerializer.Serialize(response, DomainJson.Options);
}
