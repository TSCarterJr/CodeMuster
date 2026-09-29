using CodeMuster.Application;

namespace CodeMuster.Cli.Tests;

public class StepMenuTests
{
    private readonly StringWriter output = new();

    private IReadOnlyList<AutoStep> Choose(string typed, IReadOnlyList<AutoStep>? preselected = null) =>
        new StepMenu(new StringReader(typed), output).Choose(preselected ?? AutoSteps.Defaults);

    [Fact]
    public void Enter_StartsTheTickedSteps()
    {
        Assert.Equal(AutoSteps.Defaults, Choose("\n"));
        Assert.Contains("  1 [x] doctor", output.ToString());
        Assert.Contains("  7 [ ] fix", output.ToString());
    }

    [Fact]
    public void Numbers_ToggleSteps_OneOrSeveralAtATime()
    {
        var steps = Choose("5\n7 8\n\n");

        Assert.Equal([AutoStep.Doctor, AutoStep.Scan, AutoStep.Estimate, AutoStep.Run, AutoStep.Report, AutoStep.Fix, AutoStep.Validate], steps);
    }

    [Fact]
    public void ARememberedSelection_IsWhatStartsTicked()
    {
        Assert.Equal([AutoStep.Scan, AutoStep.Report], Choose("\n", [AutoStep.Scan, AutoStep.Report]));
        Assert.Contains("  1 [ ] doctor", output.ToString());
    }

    [Fact]
    public void AnInvalidEntry_IsExplained_AndNothingIsToggled()
    {
        Assert.Equal(AutoSteps.Defaults, Choose("12\nfix\n\n"));
        Assert.Contains("type step numbers 1-8 to toggle them", output.ToString());
    }

    [Fact]
    public void NoStepsTicked_IsAskedAgain()
    {
        Assert.Equal([AutoStep.Scan], Choose("1 2 3 4 5 6\n\n2\n\n"));
        Assert.Contains("tick at least one step", output.ToString());
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("")]
    public void QuitOrTheEndOfInput_Cancels(string typed)
    {
        Assert.Throws<OperationCanceledException>(() => Choose(typed));
    }
}
