using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class SimplifyTests
{
    private const string At = "2026-09-27T00:00:00.0000000Z";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeWorkspace workspace = new();

    [Fact]
    public void SimplifyLens_HasItsPinnedInstructions()
    {
        Assert.Equal("simplify", Config.Simplify.Id);
        Assert.Empty(Config.Simplify.Globs);
        Assert.Empty(Config.Simplify.Languages);
        Assert.Equal(
            "Look only for code that could be simpler without changing what it does. Flag comments that restate the code next to them "
            + "(such as `string name; // the user's name`), commented-out code, stale comments that contradict the code, and needless complexity: "
            + "redundant conditionals, re-implementations of standard library calls, and wrappers that add nothing. "
            + "Never flag a comment that explains why the code is the way it is. "
            + "Report each finding with category \"simplification\" and severity \"low\", cite its exact lines, and say what the simpler form is.",
            Config.Simplify.Instructions);
    }

    [Fact]
    public async Task Init_WritesTheSimplifyLensIntoANewConfiguration_AfterTheDefaultLens()
    {
        var fileSystem = new FakeFileSystem();

        await new Init(fileSystem, tree).RunAsync("/repo", _ => Task.FromResult(false), CancellationToken.None);

        var written = ConfigJson.Parse(fileSystem.Files[ConfigLoader.PathFor("/repo")]);
        Assert.Equal(["default", "simplify"], written.Lenses.Select(lens => lens.Id));
        Assert.Equal(Config.DefaultInstructions, written.Lenses[0].Instructions);
        Assert.Equal(Config.Simplify.Instructions, written.Lenses[1].Instructions);
    }

    [Fact]
    public async Task Init_LeavesAnExistingConfigurationWithoutTheLensUntouched()
    {
        var fileSystem = new FakeFileSystem();
        var existing = ConfigJson.Serialize(Config.Default) + "\n";
        fileSystem.Files[ConfigLoader.PathFor("/repo")] = existing;

        var result = await new Init(fileSystem, tree).RunAsync("/repo", _ => Task.FromResult(false), CancellationToken.None);

        Assert.False(result.ConfigCreated);
        Assert.Equal(existing, fileSystem.Files[ConfigLoader.PathFor("/repo")]);
    }

    [Fact]
    public async Task Report_ListsSimplificationsInTheirOwnSection_AfterTheDefects()
    {
        var unit = AddUnit("src/a.cs");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "summary", null),
            [Defect("src/a.cs", 10), Simplification("src/a.cs", 3), Simplification("src/a.cs", 1)], CancellationToken.None);

        var report = await new Report(ledger, Config.Default).RunAsync(CancellationToken.None);

        var findings = report.IndexOf("## Findings (1)", StringComparison.Ordinal);
        var simplifications = report.IndexOf("## Simplifications (2)", StringComparison.Ordinal);
        Assert.True(findings >= 0, report);
        Assert.True(simplifications > findings, report);
        Assert.Contains("### high (1)", report[findings..simplifications]);
        Assert.DoesNotContain("remove the comment", report[findings..simplifications]);
        var section = report[simplifications..report.IndexOf("## Units", StringComparison.Ordinal)];
        Assert.True(section.IndexOf("`src/a.cs:1`", StringComparison.Ordinal) < section.IndexOf("`src/a.cs:3`", StringComparison.Ordinal), section);
        Assert.Contains("- `src/a.cs:1` [simplify, confidence 0.80, unverified] remove the comment on line 1", section);
    }

    [Fact]
    public async Task Report_WithOnlyDefects_HasNoSimplificationSection()
    {
        var unit = AddUnit("src/a.cs");
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "summary", null), [Defect("src/a.cs", 10)], CancellationToken.None);

        var report = await new Report(ledger, Config.Default).RunAsync(CancellationToken.None);

        Assert.DoesNotContain("## Simplifications", report);
    }

    [Fact]
    public async Task FixPlanning_LeavesSimplificationsOut_UnlessIncluded()
    {
        var onlySimple = AddUnit("src/simple.cs");
        var mixed = AddUnit("src/mixed.cs");
        await RecordConfirmedAsync(onlySimple, Simplification("src/simple.cs", 1));
        await RecordConfirmedAsync(mixed, Defect("src/mixed.cs", 5), Simplification("src/mixed.cs", 9));

        var excluded = await new Fix(ledger).PlanAsync(CancellationToken.None);

        Assert.Equal(1, excluded.Files);
        Assert.Equal(1, excluded.Findings);
        Assert.DoesNotContain(ledger.Units, u => u.Id == UnitIds.Fix("src/simple.cs") && u.Status != UnitStatus.Retired);

        var included = await new Fix(ledger).PlanAsync(CancellationToken.None, includeSimplification: true);

        Assert.Equal(2, included.Files);
        Assert.Equal(3, included.Findings);
    }

    [Fact]
    public async Task FixRun_AsksOnlyForTheDefects_ByDefault_AndForSimplificationsWhenIncluded()
    {
        var unit = AddUnit("src/mixed.cs");
        var ids = await RecordConfirmedAsync(unit, Defect("src/mixed.cs", 5), Simplification("src/mixed.cs", 9));
        string? firstPack = null;

        var first = await RunFixAsync(new FixOptions(), pack =>
        {
            firstPack = pack;
            return new FixResponse("fixed the defect", [ids[0]], []);
        });

        Assert.Equal(1, first.Fixed);
        Assert.Contains("claim 5", firstPack);
        Assert.DoesNotContain("remove the comment on line 9", firstPack);
        Assert.False(ledger.Fixes.ContainsKey(ids[1]));

        string? secondPack = null;
        var second = await RunFixAsync(new FixOptions { IncludeSimplification = true }, pack =>
        {
            secondPack = pack;
            return new FixResponse("removed the comment", [ids[1]], []);
        });

        Assert.Equal(1, second.Fixed);
        Assert.Contains("remove the comment on line 9", secondPack);
        Assert.Equal(FixState.Fixed, ledger.Fixes[ids[1]].State);
    }

    private Task<FixResult> RunFixAsync(FixOptions options, Func<string, FixResponse> respond)
    {
        var adapter = new FakeAgentAdapter((pack, _) =>
        {
            workspace.Clean = false;
            return Task.FromResult(FixResponseJson.Serialize(respond(pack)));
        });
        var fixer = new AdapterFixer(adapter, workspace);
        return new Fix(ledger, tree, clock, Config.Default, workspace, null, fixer).RunAsync(adapter, options, null, CancellationToken.None);
    }

    private sealed class AdapterFixer(IAgentAdapter adapter, FakeWorkspace workspace) : IFileFixer
    {
        public async Task<FileFixEdit> RunAsync(string path, string pack, CancellationToken cancellationToken)
        {
            try
            {
                var reply = await adapter.RunAsync(pack, cancellationToken);
                return new FileFixEdit(reply.Text, workspace.Clean ? "" : path) { Usage = reply.Usage };
            }
            finally
            {
                workspace.Clean = true;
            }
        }
    }

    private Unit AddUnit(string path)
    {
        tree.Add(path, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"// line {i}")));
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, null, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), UnitStatus.Done, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "content-" + path, 10, At, At, At, null, null, null, null, null, null);
        return unit;
    }

    private async Task<IReadOnlyList<long>> RecordConfirmedAsync(Unit unit, params Finding[] findings)
    {
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, "lens", At, true, "summary", null), findings, CancellationToken.None);
        var ids = (await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Where(f => f.UnitId == unit.Id).OrderBy(f => f.Finding.LineStart).Select(f => f.Id).ToList();
        foreach (var id in ids)
        {
            ledger.Verifications[id] = new VerifyResponse(Verdict.Confirmed, "it holds");
        }

        return ids;
    }

    private static Finding Defect(string path, int line) =>
        new(path, line, line, Severity.High, "correctness", $"claim {line}", "evidence", 0.9, "default");

    private static Finding Simplification(string path, int line) =>
        new(path, line, line, Severity.Low, "simplification", $"remove the comment on line {line}", "it restates the code", 0.8, "simplify");
}
