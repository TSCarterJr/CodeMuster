using System.Text.Json;

namespace CodeMuster.Domain.Tests;

public class VerifyResponseJsonTests
{
    [Fact]
    public void Parse_Sample_YieldsVerdictAndReason()
    {
        var response = VerifyResponseJson.Parse(VerifyResponseJson.Sample);

        Assert.Equal(Verdict.Refuted, response.Verdict);
        Assert.Equal("Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant.", response.Reason);
    }

    [Fact]
    public void Serialize_ParsedSample_IsByteIdenticalToSample()
    {
        Assert.Equal(VerifyResponseJson.Sample, VerifyResponseJson.Serialize(VerifyResponseJson.Parse(VerifyResponseJson.Sample)));
    }

    [Theory]
    [InlineData("confirmed", Verdict.Confirmed)]
    [InlineData("refuted", Verdict.Refuted)]
    [InlineData("unsure", Verdict.Unsure)]
    public void Parse_EveryVerdict(string text, Verdict verdict)
    {
        Assert.Equal(verdict, VerifyResponseJson.Parse($$"""{"verdict": "{{text}}", "reason": "why"}""").Verdict);
    }

    [Fact]
    public void Parse_UnknownVerdict_Throws()
    {
        Assert.Throws<JsonException>(() => VerifyResponseJson.Parse("""{"verdict": "maybe", "reason": "why"}"""));
    }

    [Fact]
    public void Parse_MissingReason_Throws()
    {
        Assert.Throws<JsonException>(() => VerifyResponseJson.Parse("""{"verdict": "confirmed"}"""));
    }

    [Fact]
    public void Parse_AnAnalysisResponse_Throws()
    {
        Assert.Throws<JsonException>(() => VerifyResponseJson.Parse(AnalysisResponseJson.Sample));
    }
}
