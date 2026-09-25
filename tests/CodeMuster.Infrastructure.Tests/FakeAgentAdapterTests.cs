using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class FakeAgentAdapterTests
{
    [Fact]
    public async Task RawResponseMode_ReturnsConfigurationWithoutAnAnalysisEnvelope()
    {
        const string response = "{\"changes\":{},\"reasons\":{}}";
        var adapter = new FakeAgentAdapter(response, "test-model", "high", rawResponse: true);

        Assert.Equal(response, await adapter.RunAsync("configuration context", CancellationToken.None));
        Assert.Equal("test-model", adapter.Identity.Model);
    }

    [Fact]
    public void DefaultTemplate_is_the_sample_formatting_with_no_findings()
    {
        Assert.Equal("{\n  \"summary\": \"fake analysis\",\n  \"findings\": []\n}", FakeAgentAdapter.DefaultTemplate);
    }

    [Fact]
    public async Task Default_template_returns_the_summary_and_no_findings()
    {
        var adapter = new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate);

        var output = await adapter.RunAsync(Pack("### src/A.cs (csharp)"), CancellationToken.None);

        Assert.Equal(FakeAgentAdapter.DefaultTemplate, output);
        Assert.Empty(AnalysisResponseJson.Parse(output).Findings);
    }

    [Fact]
    public async Task Every_finding_is_rewritten_to_the_first_file_under_files()
    {
        var first = new Finding("elsewhere/x.cs", 1, 2, Severity.High, "security", "claim one", "evidence one", 0.9, "default");
        var second = new Finding("elsewhere/y.ts", 3, 4, Severity.Low, "style", "claim two", "evidence two", 0.5, "default");
        var adapter = new FakeAgentAdapter(AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [first, second])));
        var pack = Pack("### src/Api/Quotes.cs :: Quotes.List (csharp)", "### src/Api/Other.cs (csharp)");

        var output = await adapter.RunAsync(pack, CancellationToken.None);

        var expected = new AnalysisResponse("planted", [first with { Path = "src/Api/Quotes.cs" }, second with { Path = "src/Api/Quotes.cs" }]);
        Assert.Equal(AnalysisResponseJson.Serialize(expected), output);
    }

    [Fact]
    public async Task A_lens_heading_under_instructions_is_not_mistaken_for_a_file()
    {
        var finding = new Finding("elsewhere/x.cs", 1, 2, Severity.Medium, "bug", "claim", "evidence", 0.7, "default");
        var adapter = new FakeAgentAdapter(AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [finding])));

        var response = AnalysisResponseJson.Parse(await adapter.RunAsync(Pack("### src/Only.ts (typescript)"), CancellationToken.None));

        Assert.Equal("src/Only.ts", Assert.Single(response.Findings).Path);
    }

    [Theory]
    [InlineData(0.5, Verdict.Confirmed)]
    [InlineData(0.49, Verdict.Refuted)]
    public async Task Verify_pack_confirms_a_confident_finding_and_refutes_a_doubtful_one(double confidence, Verdict verdict)
    {
        var finding = new Finding("src/A.cs", 1, 2, Severity.High, "security", "claim", "evidence", confidence, "default");
        var adapter = new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate);
        var pack = string.Join('\n',
            "# CodeMuster unit", "", "- unit: verify:1", "- kind: verify", "", "## Instructions", "", "Try to refute it.", "",
            "## Finding", "", "```json", JsonSerializer.Serialize(finding, DomainJson.Options), "```", "",
            "## Files", "", "### src/A.cs (csharp)", "", "```csharp", "class A {}", "```", "", "## Response", "");

        var output = await adapter.RunAsync(pack, CancellationToken.None);

        Assert.Equal(VerifyResponseJson.Serialize(new VerifyResponse(verdict, "fake verification")), output);
    }

    [Theory]
    [InlineData("src/Api/Quotes.cs", "class Quotes { }\n")]
    [InlineData("web/lib/api.ts", "export const api = 1;\n")]
    [InlineData("web/package.json", "{ \"name\": \"web\" }\n")]
    public async Task A_fix_pack_edits_its_file_in_the_working_directory_and_addresses_every_target(string key, string original)
    {
        using var repo = new TempRepo();
        repo.WriteFile(key, original);
        var adapter = AgentAdapters.Create("fake", null, write: true, workingDirectory: repo.Root);

        var response = FixResponseJson.Parse(await adapter.RunAsync(FixPack(key, 7, 9), CancellationToken.None));

        Assert.Equal([7L, 9L], response.Addressed);
        Assert.Empty(response.Declined);
        var edited = File.ReadAllText(Path.Combine(repo.Root, key));
        Assert.StartsWith(original, edited, StringComparison.Ordinal);
        Assert.NotEqual(original, edited);
        if (key.EndsWith(".json", StringComparison.Ordinal))
        {
            using var stillValid = JsonDocument.Parse(edited);
        }
        else
        {
            Assert.Contains("// codemuster fake fix", edited, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_fix_pack_without_write_access_edits_nothing()
    {
        using var repo = new TempRepo();
        repo.WriteFile("src/A.cs", "class A { }\n");
        var adapter = AgentAdapters.Create("fake", null, workingDirectory: repo.Root);

        await adapter.RunAsync(FixPack("src/A.cs", 1), CancellationToken.None);

        Assert.Equal("class A { }\n", File.ReadAllText(Path.Combine(repo.Root, "src", "A.cs")));
    }

    private static string FixPack(string key, params long[] ids) => string.Join('\n',
        "# CodeMuster unit", "", "- unit: fix:" + key, "- kind: fix", "- key: " + key, "", "## Instructions", "", "Fix them.", "",
        "## Findings", "", "```json", "[" + string.Join(",", ids.Select(id => "{\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")) + "]", "```", "",
        "## Files", "", "### " + key + " (csharp)", "", "```csharp", "class A {}", "```", "", "## Response", "");

    [Fact]
    public async Task Pack_without_files_throws()
    {
        var adapter = new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.RunAsync("# CodeMuster unit\n\n## Instructions\n\n### default\n\nLook.\n\n## Files\n\n## Response\n", CancellationToken.None));
    }

    [Fact]
    public void Malformed_template_is_rejected_on_construction()
    {
        Assert.Throws<JsonException>(() => new FakeAgentAdapter("{\"summary\":\"no findings key\"}"));
    }

    private static string Pack(params string[] fileHeadings)
    {
        var lines = new List<string> { "# CodeMuster unit", "", "- unit: file:src/A.cs", "", "## Instructions", "", "### default", "", "Look for bugs.", "", "## Files" };
        foreach (var heading in fileHeadings)
        {
            lines.AddRange(["", heading, "", "```csharp", "class A {}", "```"]);
        }

        lines.AddRange(["", "## Response", "", "Reply with JSON only."]);
        return string.Join('\n', lines) + "\n";
    }
}
