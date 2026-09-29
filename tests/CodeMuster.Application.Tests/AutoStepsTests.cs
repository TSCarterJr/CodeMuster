using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class AutoStepsTests
{
    [Fact]
    public void TheDefaults_AreEveryStepButFixAndValidate_InOrder()
    {
        Assert.Equal([AutoStep.Doctor, AutoStep.Scan, AutoStep.Estimate, AutoStep.Run, AutoStep.Verify, AutoStep.Report], AutoSteps.Defaults);
        Assert.Equal([AutoStep.Doctor, AutoStep.Scan, AutoStep.Estimate, AutoStep.Run, AutoStep.Verify, AutoStep.Report, AutoStep.Fix, AutoStep.Validate], AutoSteps.All);
    }

    [Fact]
    public void StepsAndSkip_AreCommaSeparatedNames_KeptInFlowOrder_AndUpdateIsTheLaunchers()
    {
        Assert.Equal([AutoStep.Scan, AutoStep.Run, AutoStep.Report], AutoSteps.Select("report, run,SCAN", null));
        Assert.Equal([AutoStep.Doctor, AutoStep.Scan, AutoStep.Estimate, AutoStep.Run, AutoStep.Report], AutoSteps.Select(null, "verify,update"));
        Assert.Equal([AutoStep.Scan, AutoStep.Fix], AutoSteps.Select("update,scan,fix", "update"));
        Assert.Equal(AutoSteps.Defaults, AutoSteps.Select(null, null));
    }

    [Fact]
    public void AnUnknownStep_NamesTheValidOnes()
    {
        var error = Assert.Throws<ArgumentException>(() => AutoSteps.Select("scan,audit", null));

        Assert.Equal("unknown step \"audit\"; steps are update, doctor, scan, estimate, run, verify, report, fix, validate", error.Message);
    }

    [Fact]
    public void OnlyRunVerifyAndFix_NeedAnAgent()
    {
        Assert.False(AutoSteps.NeedsAgent([AutoStep.Doctor, AutoStep.Scan, AutoStep.Estimate, AutoStep.Report, AutoStep.Validate]));
        Assert.True(AutoSteps.NeedsAgent([AutoStep.Verify]));
        Assert.True(AutoSteps.NeedsAgent([AutoStep.Fix]));
    }

    [Theory]
    [InlineData(0, "fix repairs only confirmed findings, and verify is not selected: 0 findings are confirmed, so fix has nothing to repair.")]
    [InlineData(3, "fix repairs only confirmed findings, and verify is not selected: fix will repair the 3 confirmed now; new findings wait for verify.")]
    public void FixWithoutVerify_Warns_WithTheConfirmedCount(int confirmed, string warning)
    {
        Assert.Equal(warning, AutoSteps.FixWarning([AutoStep.Run, AutoStep.Fix], confirmed));
        Assert.Null(AutoSteps.FixWarning([AutoStep.Run, AutoStep.Verify, AutoStep.Fix], confirmed));
        Assert.Null(AutoSteps.FixWarning([AutoStep.Run], confirmed));
    }

    [Fact]
    public async Task RunWithoutVerify_LeavesTheVerifyUnitsItCreatesQueued()
    {
        var ledger = new FakeLedger();
        var tree = new FakeSourceTree().Add("src/a.cs", "// a");
        var member = new UnitMember(UnitIds.File("src/a.cs"), "src/a.cs", null, "hash-a", 0);
        ledger.Units.Add(new Unit(member.UnitId, UnitKind.File, "src/a.cs", Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.Add(member);
        var respond = """{"summary": "A.", "findings": [{"path": "src/a.cs", "line_start": 1, "line_end": 1, "severity": "low", "category": "correctness", "claim": "c", "evidence": "e", "confidence": 0.9, "lens_id": "default"}]}""";
        var adapter = new FakeAgentAdapter((_, _) => Task.FromResult(respond));

        var result = await new Run(ledger, tree, new FakeClock(), Config.Default, adapter, new Progress<RunProgress>())
            .RunAsync(new RunOptions(1, 3, false) { SkipVerify = true }, CancellationToken.None);

        Assert.Equal(1, result.Completed);
        Assert.Single(adapter.Packs);
        Assert.Equal(UnitStatus.Pending, ledger.Units.Single(u => u.Kind == UnitKind.Verify).Status);
    }
}
