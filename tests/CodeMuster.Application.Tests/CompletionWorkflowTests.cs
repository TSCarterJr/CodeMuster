using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class CompletionWorkflowTests
{
    [Fact]
    public async Task VerificationRefresh_ReopensRetiredChecks_UsingCurrentCodeAndSameFindingId()
    {
        var ledger = new FakeLedger();
        var tree = new FakeSourceTree().Add("a.cs", "class A { int repaired; }");
        var source = new Unit("file:a.cs", UnitKind.File, "a.cs", "old", UnitStatus.Stale, Fidelity.Full, null, null, null);
        ledger.Units.Add(source);
        ledger.Members.Add(new UnitMember(source.Id, "a.cs", null, "old-hash", 0));
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, "old", "lens", "2026-09-14T00:00:00Z", true, "old", null),
            [new Finding("a.cs", 1, 1, Severity.High, "correctness", "old claim", "evidence", 0.9, "default")], CancellationToken.None);
        var verify = source with { Id = UnitIds.Verify(1), Kind = UnitKind.Verify, Status = UnitStatus.Retired };
        ledger.Units.Add(verify);
        await new RefreshVerification(ledger, tree, Config.Default, new FakeContentHasher()).RunAsync(CancellationToken.None);
        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Verify).RunAsync(1, CancellationToken.None));
        Assert.Contains("int repaired", pack.Markdown);
        Assert.NotEqual("old", pack.Fingerprint);
        Assert.Equal(1, Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Id);
    }

    [Fact]
    public async Task ResolvedVerification_RecordsFixedReason_AndLaterConfirmationReopensIt()
    {
        var ledger = new FakeLedger();
        var unit = new Unit("file:a.cs", UnitKind.File, "a.cs", "fp", UnitStatus.Done, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, "fp", "lens", "2026-09-14T00:00:00Z", true, "source", null),
            [new Finding("a.cs", 1, 1, Severity.High, "correctness", "bug", "evidence", 0.9, "default")], CancellationToken.None);
        var verify = unit with { Id = UnitIds.Verify(1), Kind = UnitKind.Verify, Key = "a.cs:1", Status = UnitStatus.Pending };
        ledger.Units.Add(verify);
        ledger.Members.Add(new UnitMember(verify.Id, "a.cs", null, "hash", 0));
        ledger.Fixes[1] = new FixOutcome(FixState.Declined, "needs a caller edit");
        var done = new Done(ledger, new FakeClock(), Config.Default);
        var result = await done.RunAsync(verify.Id, "fp", "{\"verdict\":\"resolved\",\"reason\":\"Caller now uses precise formatting at line 12.\"}", CancellationToken.None);
        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        Assert.Equal(FixState.Fixed, ledger.Fixes[1].State);
        Assert.Contains("line 12", ledger.Fixes[1].Reason);
        await done.RunAsync(verify.Id, "fp", "{\"verdict\":\"confirmed\",\"reason\":\"The bug returned.\"}", CancellationToken.None);
        Assert.False(ledger.Fixes.ContainsKey(1));
    }
}
