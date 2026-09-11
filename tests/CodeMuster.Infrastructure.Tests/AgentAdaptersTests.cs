using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class AgentAdaptersTests
{
    private const string Pack = "# CodeMuster unit\n\n## Files\n\n### src/A.cs (csharp)\n\n```csharp\nclass A {}\n```\n\n## Response\n";

    [Fact]
    public void Unknown_name_is_rejected_with_the_valid_names()
    {
        var ex = Assert.Throws<ArgumentException>(() => AgentAdapters.Create("gpt5", null));

        Assert.Contains("gpt5", ex.Message);
        Assert.Contains("fake, claude, codex, gemini, opencode", ex.Message);
    }

    [Fact]
    public async Task Fake_without_a_template_uses_the_default_template()
    {
        var adapter = Assert.IsType<FakeAgentAdapter>(AgentAdapters.Create("fake", null));

        Assert.Equal(FakeAgentAdapter.DefaultTemplate, await adapter.RunAsync(Pack, CancellationToken.None));
    }

    [Fact]
    public async Task Fake_with_a_template_uses_it()
    {
        var template = AnalysisResponseJson.Serialize(new AnalysisResponse("custom", []));

        var adapter = AgentAdapters.Create("fake", template);

        Assert.Equal("custom", AnalysisResponseJson.Parse(await adapter.RunAsync(Pack, CancellationToken.None)).Summary);
    }
}
