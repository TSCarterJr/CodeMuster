using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class ReportTests
{
    private const string Head = "abcdef0123456789abcdef0123456789abcdef01";
    private const string At = "2026-09-10T12:00:00.0000000Z";
    private const string Lens = "tenant-scoping";

    private readonly FakeLedger ledger = new();

    private Task<string> RunAsync() => new Report(ledger).RunAsync(CancellationToken.None);

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
        ledger.Runs.Add(new Run(At, Head, 3, 1, 3, null));
        var a = AddUnit("src/a.cs");
        var b = AddUnit("src/b.cs");
        AddUnit("src/c.cs", UnitStatus.Pending, Fidelity.Low);
        AddExcludedFile("src/Gen.g.cs", "generated");
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
            "analyzed 1/3 units at abcdef0, 1 stale, 1 low-fidelity, 1 files excluded\n" +
            "\n" +
            "## Findings (3)\n" +
            "\n" +
            "### high (1)\n" +
            "\n" +
            "- `src/a.cs:10-12` [tenant-scoping, confidence 0.90] Query is not tenant scoped\n" +
            "  The WHERE clause omits tenant_id.\n" +
            "\n" +
            "### medium (2)\n" +
            "\n" +
            "- `src/a.cs:30-31` [tenant-scoping, confidence 0.55] Tenant id read from the query string\n" +
            "  Line 30 trusts the tenant query parameter over the claim.\n" +
            "- `src/b.cs:1-4` [tenant-scoping, confidence 0.75] Null check missing\n" +
            "  quote may be null at line 3.\n" +
            "  (stale: unit changed since this analysis)\n" +
            "\n" +
            "## Units\n" +
            "\n" +
            "| unit | status | summary |\n" +
            "|---|---|---|\n" +
            "| src/a.cs | done | Does A |\n" +
            "| src/b.cs | stale | Does B |\n" +
            "| src/c.cs | pending |  |\n";
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
        ledger.Runs.Add(new Run(At, Head, 1, 0, 1, null));
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
            "- `src/a.cs:3-5` [tenant-scoping, confidence 0.40] Unused parameter\n" +
            "  tenantId is never read.\n" +
            "- `src/a.cs:7` [tenant-scoping, confidence 0.30] Magic number\n" +
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
