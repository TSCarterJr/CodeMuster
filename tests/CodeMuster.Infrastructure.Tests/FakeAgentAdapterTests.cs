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

        Assert.Equal(response, (await adapter.RunAsync("configuration context", CancellationToken.None)).Text);
        Assert.Equal("test-model", adapter.Identity.Model);
    }

    [Fact]
    public async Task ADelay_HoldsEachCallOpen_UntilItPassesOrTheCallIsCancelled()
    {
        var adapter = new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate, delay: TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource();

        var call = adapter.RunAsync(Pack("### src/A.cs (csharp)"), cancel.Token);
        await Task.Delay(50);
        Assert.False(call.IsCompleted);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
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

        var output = (await adapter.RunAsync(Pack("### src/A.cs (csharp)"), CancellationToken.None)).Text;

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

        var output = (await adapter.RunAsync(pack, CancellationToken.None)).Text;

        var expected = new AnalysisResponse("planted", [first with { Path = "src/Api/Quotes.cs" }, second with { Path = "src/Api/Quotes.cs" }]);
        Assert.Equal(AnalysisResponseJson.Serialize(expected), output);
    }

    [Fact]
    public async Task A_lens_heading_under_instructions_is_not_mistaken_for_a_file()
    {
        var finding = new Finding("elsewhere/x.cs", 1, 2, Severity.Medium, "bug", "claim", "evidence", 0.7, "default");
        var adapter = new FakeAgentAdapter(AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [finding])));

        var response = AnalysisResponseJson.Parse((await adapter.RunAsync(Pack("### src/Only.ts (typescript)"), CancellationToken.None)).Text);

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

        var output = (await adapter.RunAsync(pack, CancellationToken.None)).Text;

        Assert.Equal(VerifyResponseJson.Serialize(new VerifyResponse(verdict, "fake verification")), output);
    }

    [Fact]
    public async Task A_batched_analysis_pack_answers_every_unit_with_the_template_on_its_own_first_file()
    {
        var template = AnalysisResponseJson.Serialize(new AnalysisResponse("planted", [new Finding("x", 1, 1, Severity.Low, "correctness", "claim", "evidence", 0.9, "default")]));
        var adapter = new FakeAgentAdapter(template);
        var pack = string.Join('\n',
            "# CodeMuster batch", "", "This call reviews 2 separate units.", "",
            "## Unit 1 of 2: file:src/A.cs", "", "- unit: file:src/A.cs", "", "### Files", "", "#### src/A.cs (csharp)", "", "```csharp", "class A {}", "```", "",
            "## Unit 2 of 2: file:src/B.cs", "", "- unit: file:src/B.cs", "", "### Files", "", "#### src/B.cs (csharp)", "", "```csharp", "class B {}", "```", "",
            "## Response", "");

        var response = BatchAnalysisResponseJson.Parse((await adapter.RunAsync(pack, CancellationToken.None)).Text);

        Assert.Equal(["file:src/A.cs", "file:src/B.cs"], response.Units.Select(u => u.Unit));
        Assert.Equal(["src/A.cs", "src/B.cs"], response.Units.Select(u => Assert.Single(u.Findings).Path));
    }

    [Fact]
    public async Task A_batch_verify_pack_answers_every_finding_by_id_on_the_same_confidence_rule()
    {
        var confident = new Finding("src/A.cs", 1, 2, Severity.High, "security", "claim", "evidence", 0.9, "default");
        var doubtful = confident with { Confidence = 0.2 };
        var adapter = new FakeAgentAdapter(FakeAgentAdapter.DefaultTemplate);
        var pack = string.Join('\n',
            "# CodeMuster unit", "", "- unit: verify:4,9", "- kind: verify", "", "## Instructions", "", "Try to refute each.", "",
            "## Findings", "", "```json", JsonSerializer.Serialize(new[] { new { id = 4, finding = confident }, new { id = 9, finding = doubtful } }, DomainJson.Options), "```", "",
            "## Files", "", "### src/A.cs (csharp)", "", "```csharp", "class A {}", "```", "", "## Response", "");

        var output = (await adapter.RunAsync(pack, CancellationToken.None)).Text;

        Assert.Equal(VerifyBatchResponseJson.Serialize(new VerifyBatchResponse(
            [new FindingVerdict(4, Verdict.Confirmed, "fake verification"), new FindingVerdict(9, Verdict.Refuted, "fake verification")])), output);
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

        var response = FixResponseJson.Parse((await adapter.RunAsync(FixPack(key, 7, 9), CancellationToken.None)).Text);

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
