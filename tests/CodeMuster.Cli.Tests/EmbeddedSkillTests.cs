namespace CodeMuster.Cli.Tests;

public class EmbeddedSkillTests
{
    [Fact]
    public void Text_IsTheRepoSkillFile()
    {
        var expected = File.ReadAllText(Path.Combine(TempRepo.FindRepoRoot(), "skill", "SKILL.md"));

        Assert.Equal(expected.ReplaceLineEndings("\n"), EmbeddedSkill.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Text_StartsWithTheAgentSkillsFrontmatter()
    {
        Assert.StartsWith("---\nname: codemuster\n", EmbeddedSkill.Text.ReplaceLineEndings("\n"));
    }
}
