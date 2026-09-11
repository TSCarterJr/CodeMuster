using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class ReportTests
{
    private const string Head = "abcdef0123456789abcdef0123456789abcdef01";
    private const string At = "2026-09-10T12:00:00.0000000Z";
    private const string Lens = "tenant-scoping";

    private readonly FakeLedger ledger = new();

    private Task<string> RunAsync(bool includeRefuted = false) => new Report(ledger, Config.Default, includeRefuted).RunAsync(CancellationToken.None);

    private Unit AddUnit(string path, UnitStatus status = UnitStatus.Pending, Fidelity fidelity = Fidelity.Full)
    {
        var unit = new Unit(UnitIds.File(path), UnitKind.File, path, "fp-" + path, status, fidelity, null, null, null);
        ledger.Units.Add(unit);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "hash-" + path, 10, At, At, At, null, null, null, null, null, null);
        return unit;
    }

    private void AddExcludedFile(string path, string reason) =>
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "hash-" + path, 10, At, At, At, null, null, reason, null, null, null);

    private Task AnalyzeAsync(Unit unit, string summary, params Finding[] findings) =>
        ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens-hash", At, true, summary, null), findings, CancellationToken.None);

    private void MakeStale(Unit unit)
    {
        var index = ledger.Units.FindIndex(u => u.Id == unit.Id);
        ledger.Units[index] = ledger.Units[index] with { Fingerprint = unit.Fingerprint + "-changed", Status = UnitStatus.Stale };
    }

    private static Finding Finding(string path, int lineStart, int lineEnd, Severity severity, string claim, string evidence, double confidence) =>
        new(path, lineStart, lineEnd, severity, "security", claim, evidence, confidence, Lens);

    [Fact]
    public async Task SeededLedger_RendersTheGoldenMarkdown()
    {
        ledger.Runs.Add(new ScanRun(At, Head, 5, 4, 5, null));
        var e = AddUnit("src/e.cs", UnitStatus.Pending, Fidelity.Low);
        AddUnit("src/c.cs", UnitStatus.Pending, Fidelity.Low);
        var a = AddUnit("src/a.cs");
        AddUnit("src/d.cs", UnitStatus.Pending, Fidelity.Low);
        var b = AddUnit("src/b.cs");
        AddExcludedFile("src/Gen.g.cs", "generated");
        AddExcludedFile("src/Gen2.g.cs", "generated");
        AddExcludedFile("web/package-lock.json", "lockfile");
        AddExcludedFile("src/Data/Migrations/Initial.cs", "migrations");
        await AnalyzeAsync(e, "Does E");
        await AnalyzeAsync(a, "Does A",
            Finding("src/a.cs", 30, 31, Severity.Medium, "Tenant id read from the query string", "Line 30 trusts the tenant query parameter over the claim.", 0.55),
            Finding("src/a.cs", 10, 12, Severity.High, "Query is not tenant scoped", "The WHERE clause omits tenant_id.", 0.9));
        await AnalyzeAsync(b, "Does B",
            Finding("src/b.cs", 1, 4, Severity.Medium, "Null check missing", "quote may be null at line 3.", 0.75));
        MakeStale(b);

        var markdown = await RunAsync();

        const string golden =
            "# CodeMuster report\n" +
            "\n" +
            "analyzed 2/5 units at abcdef0, 1 stale, 3 low-fidelity, 4 files excluded\n" +
            "\n" +
            "## Findings (3)\n" +
            "\n" +
            "### high (1)\n" +
            "\n" +
            "- `src/a.cs:10-12` [tenant-scoping, confidence 0.90, unverified] Query is not tenant scoped\n" +
            "  The WHERE clause omits tenant_id.\n" +
            "\n" +
            "### medium (2)\n" +
            "\n" +
            "- `src/a.cs:30-31` [tenant-scoping, confidence 0.55, unverified] Tenant id read from the query string\n" +
            "  Line 30 trusts the tenant query parameter over the claim.\n" +
            "- `src/b.cs:1-4` [tenant-scoping, confidence 0.75, unverified] Null check missing\n" +
            "  quote may be null at line 3.\n" +
            "  (stale: unit changed since this analysis)\n" +
            "\n" +
            "## Units\n" +
            "\n" +
            "| unit | status | summary |\n" +
            "|---|---|---|\n" +
            "| src/a.cs | done | Does A |\n" +
            "| src/b.cs | stale | Does B |\n" +
            "| src/c.cs | pending |  |\n" +
            "| src/d.cs | pending |  |\n" +
            "| src/e.cs | done | Does E |\n";
        Assert.Equal(golden, markdown);
    }

    [Fact]
    public async Task RefutedFindings_AreLeftOut_AndTheRestShowTheirVerdict()
    {
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, null));
        var a = AddUnit("src/a.cs");
        await AnalyzeAsync(a, "Does A",
            Finding("src/a.cs", 1, 1, Severity.High, "Confirmed claim", "Evidence one.", 0.9),
            Finding("src/a.cs", 2, 2, Severity.High, "Refuted claim", "Evidence two.", 0.9),
            Finding("src/a.cs", 3, 3, Severity.Low, "Unsure claim", "Evidence three.", 0.6),
            Finding("src/a.cs", 4, 4, Severity.Low, "Unverified claim", "Evidence four.", 0.5));
        ledger.Verifications[1] = new VerifyResponse(Verdict.Confirmed, "Line 1 shows it.");
        ledger.Verifications[2] = new VerifyResponse(Verdict.Refuted, "Line 2 guards it.");
        ledger.Verifications[3] = new VerifyResponse(Verdict.Unsure, "Depends on the caller.");

        var markdown = await RunAsync();

        const string findings =
            "## Findings (3, 1 refuted not shown)\n" +
            "\n" +
            "### high (1)\n" +
            "\n" +
            "- `src/a.cs:1` [tenant-scoping, confidence 0.90, confirmed] Confirmed claim\n" +
            "  Evidence one.\n" +
            "  confirmed: Line 1 shows it.\n" +
            "\n" +
            "### low (2)\n" +
            "\n" +
            "- `src/a.cs:3` [tenant-scoping, confidence 0.60, unsure] Unsure claim\n" +
            "  Evidence three.\n" +
            "  unsure: Depends on the caller.\n" +
            "- `src/a.cs:4` [tenant-scoping, confidence 0.50, unverified] Unverified claim\n" +
            "  Evidence four.\n" +
            "\n" +
            "## Units\n";
        Assert.Contains(findings, markdown);
        Assert.DoesNotContain("Refuted claim", markdown);
    }

    [Fact]
    public async Task IncludeRefuted_ShowsRefutedFindingsWithTheirReason()
    {
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, null));
        var a = AddUnit("src/a.cs");
        await AnalyzeAsync(a, "Does A",
            Finding("src/a.cs", 1, 1, Severity.High, "Confirmed claim", "Evidence one.", 0.9),
            Finding("src/a.cs", 2, 2, Severity.High, "Refuted claim", "Evidence two.", 0.9));
        ledger.Verifications[1] = new VerifyResponse(Verdict.Confirmed, "Line 1 shows it.");
        ledger.Verifications[2] = new VerifyResponse(Verdict.Refuted, "Line 2 guards it.");

        var markdown = await RunAsync(includeRefuted: true);

        const string findings =
            "## Findings (2)\n" +
            "\n" +
            "### high (2)\n" +
            "\n" +
            "- `src/a.cs:1` [tenant-scoping, confidence 0.90, confirmed] Confirmed claim\n" +
            "  Evidence one.\n" +
            "  confirmed: Line 1 shows it.\n" +
            "- `src/a.cs:2` [tenant-scoping, confidence 0.90, refuted] Refuted claim\n" +
            "  Evidence two.\n" +
            "  refuted: Line 2 guards it.\n" +
            "\n" +
            "## Units\n";
        Assert.Contains(findings, markdown);
    }

    [Fact]
    public async Task MultiLineTextAndPipes_StayInsideTheirListItemAndTableCell()
    {
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, null));
        var a = AddUnit("src/a|b.cs");
        await AnalyzeAsync(a, "Reads quotes | writes nothing\nsecond line",
            Finding("src/a|b.cs", 2, 2, Severity.High, "Unscoped query\n\n## Injected heading", "Line 2 filters on status only.\r\n- not a list item", 0.8));

        var markdown = await RunAsync();

        const string golden =
            "# CodeMuster report\n" +
            "\n" +
            "analyzed 1/1 units at abcdef0, 0 stale, 0 low-fidelity, 0 files excluded\n" +
            "\n" +
            "## Findings (1)\n" +
            "\n" +
            "### high (1)\n" +
            "\n" +
            "- `src/a|b.cs:2` [tenant-scoping, confidence 0.80, unverified] Unscoped query ## Injected heading\n" +
            "  Line 2 filters on status only. - not a list item\n" +
            "\n" +
            "## Units\n" +
            "\n" +
            "| unit | status | summary |\n" +
            "|---|---|---|\n" +
            "| src/a\\|b.cs | done | Reads quotes \\| writes nothing second line |\n";
        Assert.Equal(golden, markdown);
    }

    [Fact]
    public async Task NoFindings_SaysNothingRecorded_AndStillListsUnits()
    {
        AddUnit("src/a.cs");

        var markdown = await RunAsync();

        const string golden =
            "# CodeMuster report\n" +
            "\n" +
            "analyzed 0/1 units at no scan yet, 0 stale, 0 low-fidelity, 0 files excluded\n" +
            "\n" +
            "## Findings (0)\n" +
            "\n" +
            "nothing recorded.\n" +
            "\n" +
            "## Units\n" +
            "\n" +
            "| unit | status | summary |\n" +
            "|---|---|---|\n" +
            "| src/a.cs | pending |  |\n";
        Assert.Equal(golden, markdown);
    }

    [Fact]
    public async Task SingleLineRange_PrintsOneLineNumber_AndFindingsSortByLineWithinAPath()
    {
        ledger.Runs.Add(new ScanRun(At, Head, 1, 0, 1, null));
        var a = AddUnit("src/a.cs");
        await AnalyzeAsync(a, "Does A",
            Finding("src/a.cs", 7, 7, Severity.Low, "Magic number", "7 is unexplained.", 0.3),
            Finding("src/a.cs", 3, 5, Severity.Low, "Unused parameter", "tenantId is never read.", 0.4));

        var markdown = await RunAsync();

        const string golden =
            "# CodeMuster report\n" +
            "\n" +
            "analyzed 1/1 units at abcdef0, 0 stale, 0 low-fidelity, 0 files excluded\n" +
            "\n" +
            "## Findings (2)\n" +
            "\n" +
            "### low (2)\n" +
            "\n" +
            "- `src/a.cs:3-5` [tenant-scoping, confidence 0.40, unverified] Unused parameter\n" +
            "  tenantId is never read.\n" +
            "- `src/a.cs:7` [tenant-scoping, confidence 0.30, unverified] Magic number\n" +
            "  7 is unexplained.\n" +
            "\n" +
            "## Units\n" +
            "\n" +
            "| unit | status | summary |\n" +
            "|---|---|---|\n" +
            "| src/a.cs | done | Does A |\n";
        Assert.Equal(golden, markdown);
    }
}
