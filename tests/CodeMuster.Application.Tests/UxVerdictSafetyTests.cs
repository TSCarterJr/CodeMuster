using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class UxVerdictSafetyTests
{
    private const string Target = "web/Invoice.tsx";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7n8AAAAASUVORK5CYII=");
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeContentHasher hasher = new();
    private readonly FakeClock clock = new();
    private readonly FakeFileSystem files = new();
    private readonly string root = Path.GetFullPath("ux-verdict-fixture");
    private static Config Enabled => Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true, BaseUrl = "http://localhost:3000" } };

    [Theory]
    [InlineData("refuted")]
    [InlineData("confirmed")]
    [InlineData("resolved")]
    public async Task DefinitiveUxVerdictsCannotBeRecordedWithoutBrowserEvidence(string verdict)
    {
        var verify = await SeedAsync();
        var pack = Assert.Single(await new Next(ledger, tree, Enabled, kind: UnitKind.Verify).RunAsync(1, CancellationToken.None));
        Assert.Contains("confirmed, refuted, or resolved", pack.Markdown);

        var result = await SubmitAsync(verify, new JsonObject { ["verdict"] = verdict, ["reason"] = "Source seems readable." });

        Assert.NotEqual(DoneOutcome.Recorded, result.Outcome);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Null(finding.Verification);
        Assert.Null(finding.Fix);
    }

    [Theory]
    [InlineData("resolved")]
    [InlineData("refuted")]
    public async Task AStillFailingSampleCannotResolveOrRefuteItsFinding(string verdict)
    {
        var verify = await SeedAsync();
        var receipt = Receipt(verify, verdict);
        FailContrast(Contrast(receipt));

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Contains("evidence", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
    }

    [Fact]
    public async Task RepairedContrastCanResolveWhileAnotherNonoverlappingSampleStillFails()
    {
        var verify = await SeedAsync(secondContrast: true);
        var receipt = Receipt(verify, "resolved");
        var other = Contrast(receipt).DeepClone().AsObject();
        other["target"] = "Separate footer link";
        other["line_start"] = 35;
        other["line_end"] = 35;
        FailContrast(other);
        Page(receipt)["readability"]!["contrast_samples"]!.AsArray().Add(other);

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        var findings = await ledger.GetCurrentFindingsAsync(CancellationToken.None);
        var finding = findings.Single(item => item.Finding.LineStart == 10);
        Assert.Equal(Verdict.Resolved, finding.Verification?.Verdict);
        Assert.Equal(FixState.Fixed, finding.Fix?.State);
        Assert.Null(findings.Single(item => item.Finding.LineStart == 35).Fix);
    }

    [Fact]
    public async Task SameObservationAtMovedLinesStillPreventsFalseResolution()
    {
        var verify = await SeedAsync();
        var receipt = Receipt(verify, "resolved");
        var sample = Contrast(receipt);
        sample["line_start"] = 35;
        sample["line_end"] = 35;
        FailContrast(sample);

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
    }

    [Theory]
    [InlineData("refuted")]
    [InlineData("confirmed")]
    [InlineData("resolved")]
    public async Task UxOriginCannotBypassEvidenceThroughGenericLabels(string verdict)
    {
        var verify = await SeedAsync(category: "maintainability", lens: "default");

        var result = await SubmitAsync(verify, new JsonObject { ["verdict"] = verdict, ["reason"] = "Source seems fine." });

        Assert.NotEqual(DoneOutcome.Recorded, result.Outcome);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Null(finding.Verification);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public async Task GenericLabelUxOriginCannotResolveAStillFailingObservation()
    {
        var verify = await SeedAsync(category: "maintainability", lens: "default");
        var receipt = Receipt(verify, "resolved");
        FailContrast(Contrast(receipt));

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
    }

    [Theory]
    [InlineData(UnitKind.DeadCode, "maintainability", "default")]
    [InlineData(UnitKind.File, "dead_code", "default")]
    [InlineData(UnitKind.File, "maintainability", "dead_code")]
    public async Task LegacyDeadCodeVerificationCannotRecordAResolvedFix(UnitKind kind, string category, string lens)
    {
        var verify = await SeedAsync(kind, category, lens);

        var result = await SubmitAsync(verify, new JsonObject { ["verdict"] = "resolved", ["reason"] = "Deleted the allegedly unused function." });

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Null(finding.Verification);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public async Task UnsureRemainsAvailableWhenTheBrowserCannotBeUsed()
    {
        var verify = await SeedAsync();

        var result = await SubmitAsync(verify, new JsonObject { ["verdict"] = "unsure", ["reason"] = "Browser authentication is unavailable." });

        Assert.Equal(DoneOutcome.Recorded, result.Outcome);
        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Equal(Verdict.Unsure, finding.Verification?.Verdict);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public async Task MissingWorkflowActionCannotBeResolvedWithTheSameMissingActionInTheReceipt()
    {
        var verify = await SeedAsync(category: "ux_workflow", line: 20, claim: "Charge Customer is missing where expected: Invoice beside the balance.");
        var receipt = Receipt(verify, "resolved");
        var action = Page(receipt)["workflow"]!["actions"]![0]!;
        action["assessment"] = "missing";
        action["observed_locations"] = new JsonArray();

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
    }

    [Fact]
    public async Task AnObservedRenderingIssueCannotBeResolvedWhileItsExperienceCheckStillFails()
    {
        var verify = await SeedAsync(category: "ux_rendering", line: 1, claim: "The outstanding balance is clipped.");
        var receipt = Receipt(verify, "resolved");
        var graphics = Page(receipt)["experience_checks"]!.AsArray().Single(check => check!["area"]!.GetValue<string>() == "graphics")!;
        graphics["assessment"] = "issue";
        graphics["evidence"] = "The outstanding balance is still clipped by the fixed-width card.";

        var result = await SubmitAsync(verify, receipt);

        Assert.Equal(DoneOutcome.Rejected, result.Outcome);
        Assert.Null(Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Fix);
    }

    private async Task<Unit> SeedAsync(UnitKind kind = UnitKind.Ux, string category = "ux_readability", string lens = UxReview.Id, int line = 10,
        string claim = "Outstanding balance has insufficient text contrast.", bool secondContrast = false)
    {
        tree.Add(Target, string.Join('\n', Enumerable.Range(1, 40).Select(index => "source line " + index)));
        await new Scan(ledger, tree, hasher, clock, Enabled).RunAsync(CancellationToken.None);
        var source = ledger.Units.Single(unit => unit.Kind == UnitKind.Ux);
        if (kind != UnitKind.Ux)
        {
            source = source with { Kind = kind };
            await ledger.UpsertUnitsAsync([source], await ledger.GetMembersAsync([source.Id], CancellationToken.None), CancellationToken.None);
        }
        var members = await ledger.GetMembersAsync([source.Id], CancellationToken.None);
        var findings = new List<Finding> { new(Target, line, line, Severity.Medium, category, claim, "Prior observation", 1, lens) };
        if (secondContrast)
            findings.Add(new Finding(Target, 35, 35, Severity.Medium, "ux_readability", "Separate footer link has insufficient text contrast.", "Other prior observation", 1, UxReview.Id));
        await ledger.RecordAnalysisAsync(new Analysis(source.Id, source.Fingerprint, "lens", "now", true, "prior review", null),
            findings, CancellationToken.None);
        var finding = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).First();
        var plan = PlannedUnit.Verify(finding, members, source.Fidelity);
        var verify = new Unit(plan.Id, UnitKind.Verify, plan.Key, Fingerprints.Compute(plan.Members), UnitStatus.Pending, plan.Fidelity, null, null, null);
        await ledger.UpsertUnitsAsync([verify], plan.Members, CancellationToken.None);
        return verify;
    }

    private JsonObject Receipt(Unit verify, string verdict)
    {
        var receipt = JsonNode.Parse("{" + UxEvidence.Sample + "}")!.AsObject();
        receipt["verdict"] = verdict;
        receipt["reason"] = "Checked the current rendered invoice.";
        receipt["ux_review"]!["fingerprint"] = verify.Fingerprint;
        receipt["ux_review"]!["runtime_source_evidence"] = "Started the fixture server from the current checkout and verified the rendered invoice state.";
        Page(receipt)["artifact_sha256"] = Convert.ToHexString(SHA256.HashData(Png));
        files.BinaryFiles[Path.GetFullPath(Path.Combine(root, Page(receipt)["artifact"]!.GetValue<string>()))] = Png;
        return receipt;
    }

    private static JsonObject Page(JsonObject receipt) => receipt["ux_review"]!["pages"]![0]!.AsObject();

    private static JsonObject Contrast(JsonObject receipt) => Page(receipt)["readability"]!["contrast_samples"]![0]!.AsObject();

    private static void FailContrast(JsonObject sample)
    {
        sample["foreground"] = "#aaaaaa";
        sample["contrast_ratio"] = 2.32;
        sample["assessment"] = "fail";
    }

    private Task<DoneResult> SubmitAsync(Unit verify, JsonObject receipt) =>
        new Done(ledger, clock, Enabled, fileSystem: files, repoRoot: root, tree: tree, hasher: hasher)
            .RunAsync(verify.Id, verify.Fingerprint, receipt.ToJsonString(), CancellationToken.None);
}
