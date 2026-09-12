using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class FixResponseJsonTests
{
    [Fact]
    public void Sample_round_trips()
    {
        var parsed = FixResponseJson.Parse(FixResponseJson.Sample);

        Assert.Equal("Scoped the query to the caller's tenant and awaited the archive call.", parsed.Summary);
        Assert.Equal([18], parsed.Addressed);
        var declined = Assert.Single(parsed.Declined);
        Assert.Equal(19, declined.Finding);
        Assert.Equal(FixResponseJson.Sample, FixResponseJson.Serialize(parsed));
    }

    [Fact]
    public void An_empty_response_parses_as_nothing_done()
    {
        var parsed = FixResponseJson.Parse("""{"summary": "Nothing to change.", "addressed": [], "declined": []}""");

        Assert.Empty(parsed.Addressed);
        Assert.Empty(parsed.Declined);
    }

    [Fact]
    public void A_missing_summary_is_rejected()
    {
        Assert.Throws<JsonException>(() => FixResponseJson.Parse("""{"addressed": [1], "declined": []}"""));
    }

    [Fact]
    public void Targets_carry_the_finding_id_and_why_it_was_confirmed()
    {
        var finding = new Finding("src/A.cs", 18, 21, Severity.High, "security", "claim", "evidence", 0.9, "default");

        var json = FixJson.SerializeTargets([new FixTarget(18, finding, "Line 19 never filters on tenant.")]);

        Assert.Contains("\"id\": 18", json);
        Assert.Contains("\"confirmed_because\": \"Line 19 never filters on tenant.\"", json);
        Assert.Contains("\"line_start\": 18", json);
    }
}
