using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class SkillInstallerTests
{
    private const string Root = "/repo";
    private const string Home = "/home/tim";
    private const string Skill = "---\nname: codemuster\ndescription: Audit a repository.\n---\n\n# CodeMuster\n";

    private readonly FakeFileSystem fileSystem = new();

    private Task<string> InstallAsync(string harness, bool global, string text = Skill) =>
        new SkillInstaller(fileSystem).InstallAsync(harness, global, Root, Home, text, CancellationToken.None);

    [Theory]
    [InlineData("claude", false, "/repo/.claude/skills/codemuster/SKILL.md")]
    [InlineData("claude", true, "/home/tim/.claude/skills/codemuster/SKILL.md")]
    [InlineData("codex", false, "/repo/.codex/skills/codemuster/SKILL.md")]
    [InlineData("codex", true, "/home/tim/.codex/skills/codemuster/SKILL.md")]
    [InlineData("gemini", false, "/repo/.gemini/skills/codemuster/SKILL.md")]
    [InlineData("gemini", true, "/home/tim/.gemini/skills/codemuster/SKILL.md")]
    [InlineData("opencode", false, "/repo/.opencode/skills/codemuster/SKILL.md")]
    [InlineData("opencode", true, "/home/tim/.config/opencode/skills/codemuster/SKILL.md")]
    public void PathFor_FollowsEachHarnessConvention(string harness, bool global, string expected)
    {
        Assert.Equal(expected, SkillInstaller.PathFor(harness, global, Root, Home).Replace('\\', '/'));
    }

    [Fact]
    public void Harnesses_ListsEveryTargetInHelpOrder()
    {
        Assert.Equal(["claude", "codex", "gemini", "opencode"], SkillInstaller.Harnesses);
    }

    [Theory]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("gemini", false)]
    [InlineData("opencode", true)]
    public async Task Install_WritesTheSkill_AndReturnsItsPath(string harness, bool global)
    {
        var path = await InstallAsync(harness, global);

        Assert.Equal(SkillInstaller.PathFor(harness, global, Root, Home), path);
        Assert.Equal(Skill, fileSystem.Files[path]);
        Assert.Equal(1, fileSystem.Writes);
        Assert.Single(fileSystem.Files);
    }

    [Fact]
    public async Task SecondInstall_LeavesFilesIdentical_AndWritesNothing()
    {
        await InstallAsync("codex", global: true);
        var before = new Dictionary<string, string>(fileSystem.Files);

        var path = await InstallAsync("codex", global: true);

        Assert.Equal(before, fileSystem.Files);
        Assert.Equal(1, fileSystem.Writes);
        Assert.Equal(SkillInstaller.PathFor("codex", true, Root, Home), path);
    }

    [Fact]
    public async Task ChangedSkillText_IsRewritten()
    {
        var path = await InstallAsync("gemini", global: false);

        await InstallAsync("gemini", global: false, text: Skill + "\nHeadless alternative: codemuster run.\n");

        Assert.Equal(Skill + "\nHeadless alternative: codemuster run.\n", fileSystem.Files[path]);
        Assert.Equal(2, fileSystem.Writes);
    }

    [Fact]
    public async Task ProjectAndGlobal_AreSeparateFiles()
    {
        var project = await InstallAsync("opencode", global: false);
        var user = await InstallAsync("opencode", global: true);

        Assert.NotEqual(project, user);
        Assert.Equal(2, fileSystem.Files.Count);
    }

    [Fact]
    public async Task UnknownHarness_Throws_AndWritesNothing()
    {
        var error = Assert.Throws<ArgumentException>(() => SkillInstaller.PathFor("cursor", false, Root, Home));
        await Assert.ThrowsAsync<ArgumentException>(() => InstallAsync("cursor", global: false));

        Assert.Equal("unknown harness 'cursor'; expected one of claude, codex, gemini, opencode", error.Message);

        Assert.Empty(fileSystem.Files);
    }
}
