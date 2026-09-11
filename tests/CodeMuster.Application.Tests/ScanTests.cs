using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class ScanTests
{
    private static readonly Config Changed =
        new([Config.Default.Lenses[0] with { Instructions = "Look only for tenant scoping mistakes." }]);

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();

    private Task<ScanResult> ScanAsync(Config? config = null, FakeContentHasher? hasher = null) =>
        new Scan(ledger, tree, hasher ?? new FakeContentHasher(), clock, config ?? Config.Default).RunAsync(CancellationToken.None);

    private Task MarkDoneAsync(string path, Config? config = null)
    {
        var unit = ledger.Units.Single(u => u.Id == UnitIds.File(path));
        var lensHash = Config.HashOf((config ?? Config.Default).LensesFor([(path, Languages.FromPath(path))]));
        var analysis = new Analysis(unit.Id, unit.Fingerprint, lensHash, Timestamps.Format(clock.UtcNow), true, "summary of " + path, null);
        return ledger.RecordAnalysisAsync(analysis, [], CancellationToken.None);
    }

    private Unit Unit(string path) => ledger.Units.Single(u => u.Id == UnitIds.File(path));

    private void AddThree()
    {
        tree.Add("src/A.cs", "class A {}");
        tree.Add("src/B.cs", "class B {}");
        tree.Add("web/c.ts", "export const c = 1;");
    }

    [Fact]
    public async Task FirstScan_CreatesOneFileUnitPerFile_AndRecordsTheRun()
    {
        AddThree();
        tree.HeadCommit = "abcdef0123456789abcdef0123456789abcdef01";

        var result = await ScanAsync();

        Assert.Equal(new ScanResult("abcdef0123456789abcdef0123456789abcdef01", 3, 0, 3, 0, 3), result);
        Assert.Equal(3, ledger.Files.Count);
        Assert.Equal(3, ledger.Units.Count);
        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Pending, u.Status));
        Assert.All(ledger.Units, u => Assert.Equal(UnitKind.File, u.Kind));
        Assert.All(ledger.Units, u => Assert.Equal(Fidelity.Full, u.Fidelity));
        Assert.Equal(Languages.CSharp, ledger.Files["src/A.cs"].Language);
        Assert.Equal(Languages.TypeScript, ledger.Files["web/c.ts"].Language);

        var unit = Unit("src/A.cs");
        var member = Assert.Single(ledger.Members, m => m.UnitId == unit.Id);
        Assert.Equal(new UnitMember(unit.Id, "src/A.cs", null, ledger.Files["src/A.cs"].ContentHash, 0), member);
        Assert.Equal(Fingerprints.Compute([member]), unit.Fingerprint);
        Assert.Equal("src/A.cs", unit.Key);

        var run = Assert.Single(ledger.Runs);
        Assert.Equal(new Domain.Run(Timestamps.Format(clock.UtcNow), "abcdef0123456789abcdef0123456789abcdef01", 3, 0, 3, null), run);
    }

    [Fact]
    public async Task SecondScan_WithNothingChanged_CreatesNothing_KeepsStatuses_AndHashesNothing()
    {
        AddThree();
        await ScanAsync();
        await MarkDoneAsync("src/A.cs");
        var hasher = new FakeContentHasher();

        var result = await ScanAsync(hasher: hasher);

        Assert.Equal(0, result.UnitsCreated);
        Assert.Equal(3, result.UnitsTotal);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(UnitStatus.Done, Unit("src/A.cs").Status);
        Assert.Equal(UnitStatus.Pending, Unit("src/B.cs").Status);
        Assert.Equal(3, ledger.Units.Count);
        Assert.Equal(3, ledger.Members.Count);
        Assert.Equal(2, ledger.Runs.Count);
    }

    [Fact]
    public async Task DirtyFile_WithUnchangedMtimeAndSize_IsNotRehashed_WhenTheRecordIsOlderThanItsMtime()
    {
        tree.AddDirty("src/A.cs", "class A {}");
        var first = new FakeContentHasher();
        await ScanAsync(hasher: first);
        Assert.Equal(1, first.Calls);
        Assert.Equal("hashed-src/A.cs", ledger.Files["src/A.cs"].ContentHash);

        var second = new FakeContentHasher();
        await ScanAsync(hasher: second);

        Assert.Equal(0, second.Calls);
        Assert.Equal("hashed-src/A.cs", ledger.Files["src/A.cs"].ContentHash);
    }

    [Fact]
    public async Task DirtyFile_WhoseRecordWasWrittenInTheSameSecondAsItsMtime_IsRehashed()
    {
        tree.AddDirty("src/A.cs", "class A {}");
        clock.UtcNow = new DateTimeOffset(2026, 9, 10, 0, 0, 0, 500, TimeSpan.Zero);
        await ScanAsync(hasher: new FakeContentHasher());
        clock.UtcNow = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        var hasher = new FakeContentHasher();
        await ScanAsync(hasher: hasher);

        Assert.Equal(1, hasher.Calls);
    }

    [Fact]
    public async Task ChangedHash_MakesExactlyThatUnitStale()
    {
        AddThree();
        await ScanAsync();
        await MarkDoneAsync("src/A.cs");
        await MarkDoneAsync("src/B.cs");
        await MarkDoneAsync("web/c.ts");
        tree.Add("src/B.cs", "class B { int x; }");

        var result = await ScanAsync();

        Assert.Equal(1, result.UnitsStale);
        Assert.Equal(0, result.UnitsCreated);
        Assert.Equal(UnitStatus.Done, Unit("src/A.cs").Status);
        Assert.Equal(UnitStatus.Stale, Unit("src/B.cs").Status);
        Assert.Equal(UnitStatus.Done, Unit("web/c.ts").Status);
        var member = Assert.Single(ledger.Members, m => m.Path == "src/B.cs");
        Assert.Equal(ledger.Files["src/B.cs"].ContentHash, member.MemberHash);
        Assert.Equal(Fingerprints.Compute([member]), Unit("src/B.cs").Fingerprint);
    }

    [Fact]
    public async Task ExcludedFiles_AreRecordedWithTheirReason_AndGetNoUnit()
    {
        tree.Add("src/A.cs", "class A {}");
        tree.Add("src/Generated.g.cs", "class G {}");
        tree.Add("src/Migrations/001_Init.cs", "class M {}");
        tree.Add("web/package-lock.json", "{}");

        var result = await ScanAsync();

        Assert.Equal(1, result.FilesIncluded);
        Assert.Equal(3, result.FilesExcluded);
        Assert.Equal(1, result.UnitsCreated);
        Assert.Equal(4, ledger.Files.Count);
        Assert.Equal("generated", ledger.Files["src/Generated.g.cs"].ExcludedReason);
        Assert.Equal("migrations", ledger.Files["src/Migrations/001_Init.cs"].ExcludedReason);
        Assert.Equal("lockfile", ledger.Files["web/package-lock.json"].ExcludedReason);
        Assert.Null(ledger.Files["src/A.cs"].ExcludedReason);
        var unit = Assert.Single(ledger.Units);
        Assert.Equal(UnitIds.File("src/A.cs"), unit.Id);
    }

    [Fact]
    public async Task RemovedFile_GetsDeletedAt_AndItsUnitIsRetired_WithRowsKept()
    {
        tree.Add("src/A.cs", "class A {}");
        tree.Add("src/B.cs", "class B {}");
        await ScanAsync();
        tree.Files.RemoveAll(f => f.Path == "src/B.cs");
        clock.UtcNow = clock.UtcNow.AddHours(1);

        var result = await ScanAsync();

        Assert.Equal(1, result.UnitsTotal);
        Assert.Equal(1, result.FilesIncluded);
        Assert.Equal(Timestamps.Format(clock.UtcNow), ledger.Files["src/B.cs"].DeletedAt);
        Assert.Null(ledger.Files["src/A.cs"].DeletedAt);
        Assert.Equal(UnitStatus.Retired, Unit("src/B.cs").Status);
        Assert.Equal(UnitStatus.Pending, Unit("src/A.cs").Status);
        Assert.Equal(2, ledger.Units.Count);
        Assert.Single(ledger.Members, m => m.Path == "src/B.cs");
        Assert.Equal(1, ledger.Runs[^1].UnitsTotal);
    }

    [Fact]
    public async Task RetiredUnit_WhoseFileReturns_BecomesPendingAgain()
    {
        tree.Add("src/A.cs", "class A {}");
        await ScanAsync();
        await MarkDoneAsync("src/A.cs");
        tree.Files.Clear();
        await ScanAsync();
        Assert.Equal(UnitStatus.Retired, Unit("src/A.cs").Status);
        tree.Add("src/A.cs", "class A {}");

        var result = await ScanAsync();

        Assert.Equal(UnitStatus.Pending, Unit("src/A.cs").Status);
        Assert.Null(ledger.Files["src/A.cs"].DeletedAt);
        Assert.Equal(1, result.UnitsCreated);
        Assert.Equal(1, result.UnitsTotal);
    }

    [Fact]
    public async Task DoneUnit_BecomesStale_WhenItsLensChanges_AndStaysDone_WhenConfigIsUnchanged()
    {
        tree.Add("src/A.cs", "class A {}");
        await ScanAsync();
        await MarkDoneAsync("src/A.cs");

        await ScanAsync();
        Assert.Equal(UnitStatus.Done, Unit("src/A.cs").Status);

        var result = await ScanAsync(Changed);

        Assert.Equal(UnitStatus.Stale, Unit("src/A.cs").Status);
        Assert.Equal(1, result.UnitsStale);
    }

    [Fact]
    public async Task StaleUnit_WhoseFileChangesAgain_StaysStale()
    {
        tree.Add("src/A.cs", "class A {}");
        await ScanAsync();
        await MarkDoneAsync("src/A.cs");
        tree.Add("src/A.cs", "class A { int x; }");
        await ScanAsync();
        Assert.Equal(UnitStatus.Stale, Unit("src/A.cs").Status);
        tree.Add("src/A.cs", "class A { int x, y; }");

        await ScanAsync();

        Assert.Equal(UnitStatus.Stale, Unit("src/A.cs").Status);
        Assert.Equal("summary of src/A.cs", Unit("src/A.cs").Summary);
    }

    [Fact]
    public async Task FirstSeen_IsPreservedAcrossScans_WhileLastSeenMoves()
    {
        tree.Add("src/A.cs", "class A {}");
        var firstScanAt = Timestamps.Format(clock.UtcNow);
        await ScanAsync();
        clock.UtcNow = clock.UtcNow.AddDays(1);

        await ScanAsync();

        Assert.Equal(firstScanAt, ledger.Files["src/A.cs"].FirstSeen);
        Assert.Equal(Timestamps.Format(clock.UtcNow), ledger.Files["src/A.cs"].LastSeen);
    }

    [Fact]
    public async Task FileSummary_IsKeptWhenTheHashIsUnchanged_AndClearedWhenItChanges()
    {
        tree.Add("src/A.cs", "class A {}");
        tree.Add("src/B.cs", "class B {}");
        await ScanAsync();
        foreach (var path in new[] { "src/A.cs", "src/B.cs" })
        {
            var record = ledger.Files[path];
            ledger.Files[path] = record with { Summary = "summary of " + path, SummaryHash = record.ContentHash };
        }

        tree.Add("src/B.cs", "class B { int x; }");

        await ScanAsync();

        Assert.Equal("summary of src/A.cs", ledger.Files["src/A.cs"].Summary);
        Assert.Equal(ledger.Files["src/A.cs"].ContentHash, ledger.Files["src/A.cs"].SummaryHash);
        Assert.Null(ledger.Files["src/B.cs"].Summary);
        Assert.Null(ledger.Files["src/B.cs"].SummaryHash);
    }
}
