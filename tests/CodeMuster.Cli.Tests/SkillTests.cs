using CodeMuster.Domain;

namespace CodeMuster.Cli.Tests;

public class SkillTests
{
    [Fact]
    public void Skill_UxChecksExperienceAndPurposeBeyondTechnicalSuccess()
    {
        Assert.Contains("Primary user flow comes first", SkillText);
        Assert.Contains("graphical/rendering defects", SkillText);
        Assert.Contains("pressed/loading feedback", SkillText);
        Assert.Contains("clipping", SkillText);
        Assert.Contains("typos", SkillText);
        Assert.Contains("validation/error recovery", SkillText);
        Assert.Contains("`experience_checks`", SkillText);
        Assert.Contains("technically works", SkillText);
        Assert.Contains("objectively illogical flow is `ux_workflow`", SkillText);
        Assert.Contains("pure taste", SkillText);
    }

    [Fact]
    public void Skill_ApplicationReviewsFollowOptInSettingsAndOnlyReviewUiForUx()
    {
        Assert.Contains("`dead_code: true`", SkillText);
        Assert.Contains("`user_experience.enabled: true`", SkillText);
        Assert.Contains("both default to off", SkillText);
        Assert.Contains("backend-only repositories are not applicable", SkillText);
        Assert.Contains("do not enable these features", SkillText);
    }

    [Fact]
    public void Skill_UxRequiresRenderedEvidenceAndDoesNotUseHeadlessCallsAsABrowserPass()
    {
        Assert.Contains("codemuster next --kind ux", SkillText);
        Assert.Contains("`ux_review`", SkillText);
        Assert.Contains("`artifact_sha256`", SkillText);
        Assert.Contains("`runtime_source_evidence`", SkillText);
        Assert.Contains("composited", SkillText);
        Assert.Contains("disabled, decorative, or logo text", SkillText);
        Assert.Contains("leaves browser units pending without a model call", SkillText);
        Assert.Contains("Do not install a browser", SkillText);
        Assert.Contains("never treat source review as a UX pass", SkillText);
    }

    [Fact]
    public void Skill_ProtectsDeadCodeAndRecommendationsAndRequiresBrowserReverification()
    {
        Assert.Contains("HTTP endpoints are externally callable", SkillText);
        Assert.Contains("`dead_code` candidates and `ux_recommendation` findings are report-only", SkillText);
        Assert.Contains("even in `review_and_fix`", SkillText);
        Assert.Contains("confirmed, refuted or resolved UX verdict requires a fresh `ux_review` receipt", SkillText);
        Assert.Contains("codemuster next --kind verify", SkillText);
        Assert.Contains("revisit the running UI after repair", SkillText);
    }

    [Fact]
    public void Skill_ExplainsSharedChangesWithoutSilentlyExpandingAutomaticTaskScope()
    {
        Assert.Contains("any included source change can make prior UX evidence stale", SkillText);
        Assert.Contains("`next --path` selects task scope", SkillText);
        Assert.Contains("report remaining stale UI targets", SkillText);
    }

    [Fact]
    public void Skill_RequiresACompatibleCliAndRechecksAfterUpdating()
    {
        var setup = SkillText[..SkillText.IndexOf("## Audit", StringComparison.Ordinal)];
        Assert.Contains("0.2.7 or later", setup);
        Assert.Contains("codemuster update", setup);
        Assert.Contains("CODEMUSTER_VERSION", setup);
        Assert.Contains("stop", setup);
    }

    private static readonly string SkillText = File.ReadAllText(Path.Combine(TempRepo.FindRepoRoot(), "skill", "SKILL.md"));

    [Fact]
    public void Skill_BootstrapsMissingCliBeforeOtherCommands_AndExplainsFailure()
    {
        var setup = SkillText[..SkillText.IndexOf("## Audit", StringComparison.Ordinal)];

        Assert.Contains("First run `codemuster --version`", setup);
        Assert.Contains("Only if the command is not found, try `npm i -g codemuster` once", setup);
        Assert.Contains("run `codemuster --version` again", setup);
        Assert.Contains("If installation is blocked or fails, npm is unavailable, or the CLI still cannot run, stop", setup);
        Assert.Contains("tell the user to run `npm i -g codemuster`", setup);
        Assert.Contains("If an existing CLI returns another error, report it instead of reinstalling", setup);
    }

    [Fact]
    public async Task Skill_PluginSetupAvoidsDuplicateProjectSkills_AndKeepsCliRecovery()
    {
        const string command = "codemuster init --yes --no-skills";
        Assert.Contains($"When using this skill through a plugin, use `{command}` instead", SkillText);
        Assert.Contains("do not combine `--for` with `--no-skills`", SkillText);
        Assert.Contains("also skips project change hooks", SkillText);
        Assert.Contains("--retry-declined", SkillText);
        Assert.Contains("--include-related", SkillText);
        Assert.Contains("codemuster validate", SkillText);
        Assert.Contains("`resolved`", SkillText);

        using var repo = TempRepo.FromFixture("mixed-repo");
        var result = await CliProcess.RunAsync(repo.Root, command.Split(' ')[1..]);
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(repo.Root, ".codemuster", "config.json")));
        foreach (var agent in new[] { ".claude", ".codex", ".gemini" })
            Assert.False(Directory.Exists(Path.Combine(repo.Root, agent)));
    }

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
        Assert.True(lines.Length <= 80, $"SKILL.md is {lines.Length} lines; keep the shared workflow concise");
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
