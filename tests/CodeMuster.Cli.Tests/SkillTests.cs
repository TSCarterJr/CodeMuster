using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class SkillTests
{
    private static readonly string SkillText = File.ReadAllText(Path.Combine(TempRepo.FindRepoRoot(), "skill", "SKILL.md"));

    [Fact]
    public void Skill_ContainsTheExactResponseSample()
    {
        Assert.Contains("```json\n" + AnalysisResponseJson.Sample + "\n```", SkillText.ReplaceLineEndings("\n"));
        Assert.Contains("```json\n" + VerifyResponseJson.Sample + "\n```", SkillText.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Skill_HasAgentSkillsFrontmatter_AndStaysShort()
    {
        var lines = SkillText.ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("---", lines[0]);
        Assert.StartsWith("name: codemuster", lines[1]);
        Assert.StartsWith("description: ", lines[2]);
        Assert.Equal("---", lines[3]);
        Assert.True(lines.Length <= 60, $"SKILL.md is {lines.Length} lines; keep it under one screen");
    }

    [Theory]
    [InlineData("codemuster init --for codex --yes")]
    [InlineData("codemuster scan")]
    [InlineData("codemuster next")]
    [InlineData("codemuster done")]
    [InlineData("codemuster status")]
    [InlineData("every finding must cite a path listed under Files")]
    public void Skill_MentionsEveryStepOfTheLoop(string text)
    {
        Assert.Contains(text, SkillText);
    }
}
