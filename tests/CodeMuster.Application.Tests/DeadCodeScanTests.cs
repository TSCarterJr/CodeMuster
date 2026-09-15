using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DeadCodeScanTests
{
    private const string At = "2026-09-15T00:00:00.0000000Z";
    private static Config Enabled => Config.Default with { DeadCode = true };

    [Fact]
    public async Task ScanProtectsBrowserEndpointAndReportsOnlyTheInternalCandidate()
    {
        var sources = Sources();
        var scan = DeadCodeScan.Build(Map(), Files(sources), sources);
        var ledger = new FakeLedger();
        await SaveAsync(scan, ledger);

        var finding = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Equal("api/Invoices.cs", finding.Finding.Path);
        Assert.Equal("dead_code", finding.Finding.Category);
        Assert.Equal("dead_code", finding.Finding.LensId);
        Assert.Equal(Severity.Info, finding.Finding.Severity);
        Assert.Contains("unused", finding.Finding.Claim, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not proof", finding.Finding.Evidence);
        Assert.Null(finding.Verification);
        Assert.DoesNotContain(ledger.Units, unit => unit.Kind == UnitKind.Verify);
        Assert.All(ledger.Units, unit => Assert.Equal(UnitKind.DeadCode, unit.Kind));

        var evidence = (await ledger.GetLatestEvidenceAsync(CancellationToken.None))[UnitIds.DeadCode("api/Invoices.cs")].EvidenceJson!;
        using var json = JsonDocument.Parse(evidence);
        var endpoint = json.RootElement.GetProperty("assessments").EnumerateArray().Single(item => item.GetProperty("symbol_id").GetString() == "endpoint");
        Assert.Equal("protected_entry_point", endpoint.GetProperty("state").GetString());
        Assert.Contains("web/Invoice.tsx", endpoint.GetProperty("usage_evidence").ToString());
    }

    [Fact]
    public async Task AddingACallerChangesFingerprintAndReplacesTheOldCandidate()
    {
        var sources = Sources();
        var ledger = new FakeLedger();
        var first = DeadCodeScan.Build(Map(), Files(sources), sources);
        await SaveAsync(first, ledger);
        var fingerprint = ledger.Units.Single(unit => unit.Key == "api/Invoices.cs").Fingerprint;
        Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));

        sources["api/Job.cs"] = "public class Job { public void Execute() { Invoices.Helper(); } }";
        var mapped = Map();
        mapped = mapped with
        {
            Map = mapped.Map with
            {
                Symbols = [.. mapped.Map.Symbols, new Symbol("job", "api/Job.cs", new LineRange(1, 1), "method", "public void Execute()", "job-hash")],
                Edges = [new Edge("job", "helper", EdgeKind.Call)]
            }
        };
        await SaveAsync(DeadCodeScan.Build(mapped, Files(sources), sources), ledger);

        Assert.NotEqual(fingerprint, ledger.Units.Single(unit => unit.Key == "api/Invoices.cs").Fingerprint);
        Assert.Empty(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Equal(2, ledger.Analyses.Count(attempt => attempt.Analysis.UnitId == UnitIds.DeadCode("api/Invoices.cs")));
    }

    [Fact]
    public async Task MissingMapsRecordUnknownEvidenceWithoutDeadCodeFindings()
    {
        var sources = new Dictionary<string, string> { ["api/Invoices.cs"] = "class Invoices { private void Helper() { } }" };
        var mapped = new CompositeMap(new CodeMap([], [], [], new ResolutionStats(0, 0, []), []), [], []);
        var scan = DeadCodeScan.Build(mapped, Files(sources), sources);
        var ledger = new FakeLedger();
        await SaveAsync(scan, ledger);

        Assert.Equal(Fidelity.Low, Assert.Single(scan.Plans).Fidelity);
        Assert.Empty(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        var analysis = Assert.Single(await ledger.GetLatestEvidenceAsync(CancellationToken.None)).Value;
        Assert.Contains("unavailable", analysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", analysis.EvidenceJson);
        Assert.DoesNotContain("safe to delete", analysis.EvidenceJson);
    }

    [Fact]
    public async Task UnchangedScanPreservesFindingIdsAndAnalysisHistory()
    {
        var sources = Sources();
        var ledger = new FakeLedger();
        await SaveAsync(DeadCodeScan.Build(Map(), Files(sources), sources), ledger);
        var original = Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        var attempts = ledger.Analyses.Count;

        await SaveAsync(DeadCodeScan.Build(Map(), Files(sources), sources), ledger);

        Assert.Equal(attempts, ledger.Analyses.Count);
        Assert.Equal(original, Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task MapperRecoveryReplacesUnknownEvenWhenSourceDidNotChange()
    {
        var sources = Sources();
        var ledger = new FakeLedger();
        var good = Map();
        var unavailable = good with { Map = good.Map with { Diagnostics = ["mapper incomplete"] } };
        await SaveAsync(DeadCodeScan.Build(unavailable, Files(sources), sources), ledger);
        var original = ledger.Units.Single(unit => unit.Key == "api/Invoices.cs").Fingerprint;
        Assert.Empty(await ledger.GetCurrentFindingsAsync(CancellationToken.None));

        await SaveAsync(DeadCodeScan.Build(good, Files(sources), sources), ledger);

        Assert.NotEqual(original, ledger.Units.Single(unit => unit.Key == "api/Invoices.cs").Fingerprint);
        Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LensChangesRerecordButFileAndMapOrderDoNot()
    {
        var sources = Sources();
        var ledger = new FakeLedger();
        var first = DeadCodeScan.Build(Map(), Files(sources), sources);
        await SaveAsync(first, ledger);
        Assert.NotEmpty(ledger.Analyses);
        var attempts = ledger.Analyses.Count;
        var mapped = Map();
        mapped = mapped with { Map = mapped.Map with { Symbols = mapped.Map.Symbols.Reverse().ToList() }, MappedLanguages = mapped.MappedLanguages.Reverse().ToList() };
        var reversed = DeadCodeScan.Build(mapped, Files(sources).Reverse().ToList(), sources.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value));
        await SaveAsync(reversed, ledger);
        Assert.Equal(attempts, ledger.Analyses.Count);

        await reversed.RecordAsync(ledger, Enabled with { Lenses = [new Lens("changed", "Different review instructions", [], [])] }, At, CancellationToken.None);
        Assert.Equal(attempts * 2, ledger.Analyses.Count);
    }

    [Fact]
    public async Task UnmappedCodeAndMissingSourcePreventFalseUnusedCandidates()
    {
        var sources = Sources();
        sources["worker.py"] = "invoke_from_config()";
        var ledger = new FakeLedger();
        await SaveAsync(DeadCodeScan.Build(Map(), Files(sources), sources), ledger);
        Assert.Empty(await ledger.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Contains(ledger.Units, unit => unit.Key == "worker.py");

        var files = Files(Sources());
        sources = Sources();
        sources.Remove("api/Invoices.cs");
        var incomplete = DeadCodeScan.Build(Map(), files, sources);
        var missing = new FakeLedger();
        await SaveAsync(incomplete, missing);
        Assert.Empty(await missing.GetCurrentFindingsAsync(CancellationToken.None));
        Assert.Contains("unavailable", (await missing.GetLatestEvidenceAsync(CancellationToken.None))[UnitIds.DeadCode("api/Invoices.cs")].EvidenceJson);
    }

    [Fact]
    public void ExcludedAndDeletedFilesNeverGetUnitsButNonCodeFilesContributeContext()
    {
        var sources = Sources();
        sources["README.md"] = "Configuration registration lives here.";
        var files = Files(sources).ToList();
        files.Add(File("excluded.cs", "class Excluded { }") with { ExcludedReason = "exclude:excluded.cs" });
        files.Add(File("deleted.ts", "function deleted() { }") with { DeletedAt = At });
        var first = DeadCodeScan.Build(Map(), files, sources);
        Assert.DoesNotContain(first.Plans, plan => plan.Key is "README.md" or "excluded.cs" or "deleted.ts");
        sources["README.md"] += " New context.";
        var changed = DeadCodeScan.Build(Map(), Files(sources), sources);
        Assert.NotEqual(Fingerprints.Compute(first.Plans[0].Members), Fingerprints.Compute(changed.Plans[0].Members));
    }

    [Fact]
    public async Task CannotRecordAPlanAgainstADifferentCurrentFingerprint()
    {
        var sources = Sources();
        var scan = DeadCodeScan.Build(Map(), Files(sources), sources);
        var ledger = new FakeLedger();
        await SaveAsync(scan, ledger);
        ledger.Units[0] = ledger.Units[0] with { Fingerprint = "newer scan", Status = UnitStatus.Stale };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scan.RecordAsync(ledger, Enabled, At, CancellationToken.None));
    }

    private static async Task SaveAsync(DeadCodeScan scan, FakeLedger ledger)
    {
        var existing = ledger.Units.ToDictionary(unit => unit.Id);
        await ledger.UpsertUnitsAsync(scan.Plans.Select(plan =>
        {
            existing.TryGetValue(plan.Id, out var previous);
            return new Unit(plan.Id, plan.Kind, plan.Key, Fingerprints.Compute(plan.Members), previous?.Status ?? UnitStatus.Pending, plan.Fidelity,
                previous?.LensHash, previous?.Summary, previous?.SummaryHash);
        }).ToList(), scan.Plans.SelectMany(plan => plan.Members).ToList(), CancellationToken.None);
        await scan.RecordAsync(ledger, Enabled, At, CancellationToken.None);
    }

    private static Dictionary<string, string> Sources() => new(StringComparer.Ordinal)
    {
        ["api/Invoices.cs"] = "public class Invoices { public void Get() { } private void Helper() { } }",
        ["web/Invoice.tsx"] = "export function Invoice() { return fetch('/invoices'); }"
    };

    private static CompositeMap Map() => new(new CodeMap(
        [new Symbol("endpoint", "api/Invoices.cs", new LineRange(1, 1), "method", "public void Get()", "endpoint-hash"),
         new Symbol("helper", "api/Invoices.cs", new LineRange(1, 1), "method", "private void Helper()", "helper-hash"),
         new Symbol("page", "web/Invoice.tsx", new LineRange(1, 1), "function", "export function Invoice()", "page-hash")],
        [], [new EntryPoint("endpoint", "http", "GET /invoices")], new ResolutionStats(0, 0, []), []), [], [Languages.CSharp, Languages.TypeScript]);

    private static IReadOnlyList<FileRecord> Files(IReadOnlyDictionary<string, string> sources) => sources.Select(pair => File(pair.Key, pair.Value)).ToList();

    private static FileRecord File(string path, string source) => new(path, Languages.FromPath(path), Hashing.Sha256Hex(source), source.Length, At, At, At, null, null, null, null, null, null);
}
