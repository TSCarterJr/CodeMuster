namespace CodeMuster.Domain.Tests;

public class ResponseTextTests
{
    private const string Json = """{"summary": "Nothing to report.", "findings": []}""";

    [Fact]
    public void ExtractJson_StripsJsonFence()
    {
        Assert.Equal(Json, ResponseText.ExtractJson("```json\n" + Json + "\n```\n"));
    }

    [Fact]
    public void ExtractJson_StripsFence_AroundMultiLineSample()
    {
        Assert.Equal(AnalysisResponseJson.Sample, ResponseText.ExtractJson("```json\n" + AnalysisResponseJson.Sample + "\n```"));
    }

    [Fact]
    public void ExtractJson_DropsProseBeforeAndAfter()
    {
        var text = "Here is my analysis of the unit.\n\n" + Json + "\n\nLet me know if you want more detail.";

        Assert.Equal(Json, ResponseText.ExtractJson(text));
    }

    [Fact]
    public void ExtractJson_DropsProseAroundFencedJson()
    {
        var text = "Sure! Here it is:\n\n```json\n" + Json + "\n```\n\nDone.";

        Assert.Equal(Json, ResponseText.ExtractJson(text));
    }

    [Fact]
    public void ExtractJson_LeavesBareJsonUnchanged()
    {
        Assert.Equal(Json, ResponseText.ExtractJson(Json));
    }

    [Fact]
    public void ExtractJson_KeepsNestedBraces_ThroughTheLastClosingBrace()
    {
        const string nested = """{"summary": "a {b} c", "findings": [{"path": "x"}]}""";

        Assert.Equal(nested, ResponseText.ExtractJson("```json\n" + nested + "\n```"));
    }

    [Fact]
    public void ExtractJson_ReturnsTrimmedInput_WhenNoBraces()
    {
        Assert.Equal("I could not analyze this unit.", ResponseText.ExtractJson("  I could not analyze this unit. \n"));
    }

    [Fact]
    public void ExtractJson_ReturnsTrimmedInput_WhenOnlyAnOpeningBrace()
    {
        Assert.Equal("{\"summary\":", ResponseText.ExtractJson("{\"summary\":\n"));
    }
}
