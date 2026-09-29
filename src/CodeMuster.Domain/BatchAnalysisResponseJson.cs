using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>JSON contract for <see cref="BatchAnalysisResponse"/>: the sample a batched pack shows the model, plus parse and serialize (D80).</summary>
public static class BatchAnalysisResponseJson
{
    /// <summary>The exact response text a batched pack asks the model to produce.</summary>
    public const string Sample = """
        {
          "units": [
            {
              "unit": "file:src/Billing/InvoiceRepository.cs",
              "summary": "Loads and saves invoices for the current tenant.",
              "findings": [
                {
                  "path": "src/Billing/InvoiceRepository.cs",
                  "line_start": 42,
                  "line_end": 44,
                  "severity": "high",
                  "category": "security",
                  "claim": "The Status filter runs before the tenant filter, so another tenant's invoices can be returned.",
                  "evidence": "Line 42 queries by Status; the TenantId filter on line 44 is applied to the materialized list.",
                  "confidence": 0.8,
                  "lens_id": "default"
                }
              ]
            },
            {
              "unit": "file:src/Billing/InvoiceTotals.cs",
              "summary": "Sums invoice lines with tax.",
              "findings": []
            }
          ]
        }
        """;

    /// <summary>Parses a model response; any malformed, missing, or unknown value surfaces as a <see cref="JsonException"/>.</summary>
    public static BatchAnalysisResponse Parse(string json) =>
        JsonSerializer.Deserialize<BatchAnalysisResponse>(json, DomainJson.Options)
        ?? throw new JsonException("response is null");

    /// <summary>Serializes a response in the same shape as <see cref="Sample"/>.</summary>
    public static string Serialize(BatchAnalysisResponse response) =>
        JsonSerializer.Serialize(response, DomainJson.Options);
}
