using System.Text.Json;

namespace CodeMuster.Domain.Tests;

public class AnalysisResponseJsonTests
{
    [Fact]
    public void Parse_Sample_YieldsSummaryAndOneFinding()
    {
        var response = AnalysisResponseJson.Parse(AnalysisResponseJson.Sample);

        Assert.Equal("Repository for invoices; every query is expected to be scoped to the caller's tenant.", response.Summary);
        var finding = Assert.Single(response.Findings);
        Assert.Equal("src/Billing/InvoiceRepository.cs", finding.Path);
        Assert.Equal(18, finding.LineStart);
        Assert.Equal(21, finding.LineEnd);
        Assert.Equal(Severity.High, finding.Severity);
        Assert.Equal("security", finding.Category);
        Assert.Equal("ListOpen returns invoices for every tenant because the query has no TenantId filter.", finding.Claim);
        Assert.Equal("The Where clause filters on Status only; TenantId is never referenced.", finding.Evidence);
        Assert.Equal(0.9, finding.Confidence);
        Assert.Equal("default", finding.LensId);
    }

    [Fact]
    public void RoundTrip_TwoFindings_PreservesEveryField()
    {
        var original = new AnalysisResponse(
            "Two findings.",
            [
                new Finding("src/a.cs", 1, 2, Severity.Critical, "security", "claim a", "evidence a", 0.75, "default"),
                new Finding("src/b/c.ts", 10, 10, Severity.Info, "style", "claim b", "evidence b", 0.2, "perf"),
            ]);

        var parsed = AnalysisResponseJson.Parse(AnalysisResponseJson.Serialize(original));

        Assert.Equal(original.Summary, parsed.Summary);
        Assert.Equal(original.Findings, parsed.Findings);
    }

    [Fact]
    public void Serialize_ParsedSample_IsByteIdenticalToSample()
    {
        var text = AnalysisResponseJson.Serialize(AnalysisResponseJson.Parse(AnalysisResponseJson.Sample));

        Assert.Equal(AnalysisResponseJson.Sample, text);
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        const string json = """
            {
              "summary": "Missing claim.",
              "findings": [
                {
                  "path": "src/a.cs",
                  "line_start": 1,
                  "line_end": 2,
                  "severity": "low",
                  "category": "style",
                  "evidence": "evidence",
                  "confidence": 0.5,
                  "lens_id": "default"
                }
              ]
            }
            """;

        Assert.Throws<JsonException>(() => AnalysisResponseJson.Parse(json));
    }

    [Fact]
    public void Parse_UnknownSeverity_Throws()
    {
        var json = AnalysisResponseJson.Sample.Replace("\"high\"", "\"catastrophic\"");

        Assert.Throws<JsonException>(() => AnalysisResponseJson.Parse(json));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<JsonException>(() => AnalysisResponseJson.Parse("{\"summary\": "));
    }

    [Fact]
    public void Parse_NullLiteral_Throws()
    {
        Assert.Throws<JsonException>(() => AnalysisResponseJson.Parse("null"));
    }

    [Fact]
    public void Parse_EmptyFindings_YieldsNoFindings()
    {
        var response = AnalysisResponseJson.Parse("""{"summary": "Nothing to report.", "findings": []}""");

        Assert.Empty(response.Findings);
    }
}
