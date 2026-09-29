using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class RunBatchTests
{
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly List<RunProgress> reports = [];

    private void AddFileUnit(string path, string? content = null)
    {
        tree.Add(path, content ?? $"// {path}");
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        ledger.Units.Add(new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.Add(member);
    }

    private Task<RunResult> RunAsync(FakeAgentAdapter adapter, Config? config = null) =>
        new Run(ledger, tree, clock, config ?? Config.Default, adapter, new Recording(reports)).RunAsync(new RunOptions(1, 3, false), CancellationToken.None);

    private static IReadOnlyList<string> UnitIdsIn(string pack) =>
        [.. pack.Split('\n').Where(line => line.StartsWith("- unit: ", StringComparison.Ordinal)).Select(line => line["- unit: ".Length..])];

    private static string Answer(IEnumerable<string> unitIds) =>
        BatchAnalysisResponseJson.Serialize(new BatchAnalysisResponse([.. unitIds.Select(id => new UnitAnalysis(id, "Nothing to report.", []))]));

    // Answers a batch for every unit it lists and a single pack in the single shape.
    private static FakeAgentAdapter Answering(Func<IReadOnlyList<string>, IEnumerable<string>>? keep = null) => new((pack, _) =>
    {
        var ids = UnitIdsIn(pack);
        return Task.FromResult(pack.StartsWith("# CodeMuster batch", StringComparison.Ordinal)
            ? Answer(keep is null ? ids : keep(ids))
            : """{"summary": "Nothing to report.", "findings": []}""");
    });

    private UnitStatus StatusOf(string id) => ledger.Units.Single(u => u.Id == id).Status;

    [Fact]
    public async Task SmallUnitsInOneDirectory_ShareACall_AndEachKeepsItsOwnAnalysis()
    {
        foreach (var name in new[] { "a", "b", "c", "d", "e" }) AddFileUnit($"src/{name}.cs");
        AddFileUnit("lib/f.cs");
        var adapter = Answering();

        var result = await RunAsync(adapter);

        Assert.Equal(6, result.Completed);
        Assert.Equal(
            [["file:src/a.cs", "file:src/b.cs", "file:src/c.cs", "file:src/d.cs"], ["file:lib/f.cs"], ["file:src/e.cs"]],
            adapter.Packs.Select(UnitIdsIn).OrderByDescending(ids => ids.Count).ThenBy(ids => ids[0], StringComparer.Ordinal).ToList());
        Assert.All(ledger.Units, u => Assert.Equal(UnitStatus.Done, u.Status));
        Assert.Equal(6, ledger.Analyses.Count(a => a.Analysis.Succeeded));
    }

    [Fact]
    public async Task TheBatchPack_ShowsEachUnitsOwnPack_WithOneCombinedResponse()
    {
        AddFileUnit("src/a.cs", "class A { }");
        AddFileUnit("src/b.cs", "class B { }");
        var adapter = Answering();

        await RunAsync(adapter);

        var pack = Assert.Single(adapter.Packs);
        Assert.StartsWith("# CodeMuster batch\n", pack);
        Assert.Contains("\n## Unit 1 of 2: file:src/a.cs\n", pack);
        Assert.Contains("\n## Unit 2 of 2: file:src/b.cs\n", pack);
        Assert.Contains("\n### Instructions\n", pack);
        Assert.Contains("class A { }", pack);
        Assert.Contains("class B { }", pack);
        Assert.Contains(BatchAnalysisResponseJson.Sample, pack);
        Assert.Single(pack.Split("\n## Response\n").Skip(1));
    }

    [Fact]
    public async Task AUnitTheResponseLeavesOut_IsRetriedAlone()
    {
        foreach (var name in new[] { "a", "b", "c" }) AddFileUnit($"src/{name}.cs");
        var adapter = Answering(ids => ids.Where(id => id != "file:src/b.cs"));

        var result = await RunAsync(adapter);

        Assert.Equal(3, result.Completed);
        Assert.Equal([3, 1], adapter.Packs.Select(pack => UnitIdsIn(pack).Count));
        Assert.Equal(["file:src/b.cs"], UnitIdsIn(adapter.Packs[1]));
        Assert.Contains(reports, r => r.UnitId == "file:src/b.cs" && r.Outcome == DoneOutcome.Rejected && r.Message.Contains("left this unit out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABatchWhoseResponseDoesNotParse_IsRetriedUnitByUnit()
    {
        foreach (var name in new[] { "a", "b" }) AddFileUnit($"src/{name}.cs");
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(pack.StartsWith("# CodeMuster batch", StringComparison.Ordinal)
            ? "not json"
            : """{"summary": "Nothing to report.", "findings": []}"""));

        var result = await RunAsync(adapter);

        Assert.Equal(2, result.Completed);
        Assert.Equal([2, 1, 1], adapter.Packs.Select(pack => UnitIdsIn(pack).Count));
    }

    [Fact]
    public async Task DifferentLensSets_LargePacks_AndBatchUnitsOne_AreNotBatched()
    {
        AddFileUnit("src/a.cs");
        AddFileUnit("src/b.cs");
        AddFileUnit("src/big.cs", string.Join('\n', Enumerable.Range(0, 300).Select(n => "// filler line " + n)));
        var config = Config.Default with { SliceTokenBudget = 3000, Lenses = [.. Config.Default.Lenses, new Lens("only-a", "Look at a.", ["src/a.cs"], [])] };
        var adapter = Answering();

        await RunAsync(adapter, config);

        Assert.Equal(3, adapter.Packs.Count);
        Assert.All(adapter.Packs, pack => Assert.Single(UnitIdsIn(pack)));
        Assert.All(reports, r => Assert.Equal(DoneOutcome.Recorded, r.Outcome));

        foreach (var unit in ledger.Units.ToList()) ledger.Units[ledger.Units.IndexOf(unit)] = unit with { Status = UnitStatus.Pending };
        var single = Answering();
        await RunAsync(single, Config.Default with { BatchUnits = 1 });
        Assert.Equal(3, single.Packs.Count);
    }

    [Fact]
    public async Task ABatchCallsUsage_IsSplitAcrossItsUnits()
    {
        AddFileUnit("src/a.cs");
        AddFileUnit("src/b.cs");
        var adapter = Answering();
        adapter.Usage = new AgentUsage(1000, 100, 0, 0, "claude-opus-5-5", 0.50m);

        await RunAsync(adapter);

        Assert.Equal(["file:src/a.cs", "file:src/b.cs"], ledger.Calls.Select(c => c.UnitId).Order(StringComparer.Ordinal));
        Assert.Equal(1000, ledger.Calls.Sum(c => c.Usage.InputTokens));
        Assert.Equal(0.50m, ledger.Calls.Sum(c => c.Usage.ReportedCostUsd));
    }

    [Fact]
    public void ConfigJson_ReadsBatchUnits_AndRejectsLessThanOne()
    {
        Assert.Equal(4, Config.Default.BatchUnits);
        Assert.Equal(2, ConfigJson.Parse("""{ "lenses": [], "batch_units": 2 }""").BatchUnits);
        Assert.Throws<System.Text.Json.JsonException>(() => ConfigJson.Parse("""{ "lenses": [], "batch_units": 0 }"""));
    }

    private sealed class Recording(List<RunProgress> reports) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => reports.Add(value);
    }
}
