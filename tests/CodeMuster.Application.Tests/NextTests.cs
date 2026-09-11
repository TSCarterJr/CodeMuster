using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class NextTests
{
    private static readonly Lens Tenancy = new("tenancy", "Every query must filter by tenant.", ["web/**"], []);

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();

    private Unit AddFileUnit(string path, string content, UnitStatus status = UnitStatus.Pending, string? symbol = null)
    {
        tree.Add(path, content);
        var id = UnitIds.File(path);
        var member = new UnitMember(id, path, symbol, "hash-" + path, 0);
        var unit = new Unit(id, UnitKind.File, path, Fingerprints.Compute([member]), status, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.Add(member);
        return unit;
    }

    private Unit AddUnit(string id, UnitKind kind, string key, params UnitMember[] members)
    {
        var unit = new Unit(id, kind, key, Fingerprints.Compute(members), UnitStatus.Pending, Fidelity.Full, null, null, null);
        ledger.Units.Add(unit);
        ledger.Members.AddRange(members);
        return unit;
    }

    private Task<IReadOnlyList<UnitPack>> RunAsync(int batch = 1, Config? config = null) =>
        new Next(ledger, tree, config ?? Config.Default).RunAsync(batch, CancellationToken.None);

    [Fact]
    public async Task Pack_MatchesExactLayout()
    {
        var unit = AddFileUnit("src/A.cs", "class A { }");

        var pack = Assert.Single(await RunAsync());

        var expected = $$"""
            # CodeMuster unit

            - unit: file:src/A.cs
            - kind: file
            - key: src/A.cs
            - fingerprint: {{unit.Fingerprint}}
            - lenses: default

            ## Instructions

            ### default

            {{Config.DefaultInstructions}}

            ## Files

            ### src/A.cs (csharp)

            ```csharp
            class A { }
            ```

            ## Response

            Reply with JSON only, in exactly this shape:

            ```json
            {{AnalysisResponseJson.Sample}}
            ```

            Every finding must cite a path listed under Files and set lens_id to the lens it came from. Then record it with:

                codemuster done file:src/A.cs --fingerprint {{unit.Fingerprint}} --findings <path-to-your-json-file>

            """;
        Assert.Equal(expected, pack.Markdown);
    }

    [Fact]
    public async Task Pack_ContainsFileContentVerbatim_UnderPathHeading()
    {
        var content = "namespace A;\n\npublic class Thing\n{\n    public int X => 1;\n}\n";
        AddFileUnit("src/A.cs", content);

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("### src/A.cs (csharp)\n\n```csharp\n" + content + "\n```\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_CarriesUnitIdAndFingerprint()
    {
        var unit = AddFileUnit("src/A.cs", "class A;");

        var pack = Assert.Single(await RunAsync());

        Assert.Equal(unit.Id, pack.UnitId);
        Assert.Equal(unit.Fingerprint, pack.Fingerprint);
        Assert.Contains($"- unit: {unit.Id}\n", pack.Markdown);
        Assert.Contains($"- fingerprint: {unit.Fingerprint}\n", pack.Markdown);
        Assert.Contains($"    codemuster done {unit.Id} --fingerprint {unit.Fingerprint} --findings <path-to-your-json-file>\n", pack.Markdown);
    }

    [Fact]
    public async Task HeadlessPack_AsksForJsonOnly_WithoutTheDoneCommand()
    {
        var unit = AddFileUnit("src/A.cs", "class A;");

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, interactive: false).RunAsync(1, CancellationToken.None));

        Assert.Contains("Print the JSON and nothing else; the driver records it for you.\n", pack.Markdown);
        Assert.DoesNotContain("codemuster done", pack.Markdown);
        Assert.Contains($"- fingerprint: {unit.Fingerprint}\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_IncludesDefaultLensInstructions()
    {
        AddFileUnit("src/A.cs", "class A;");

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("- lenses: default\n", pack.Markdown);
        Assert.Contains("### default\n\n" + Config.DefaultInstructions + "\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_OmitsLensWhoseGlobDoesNotMatch()
    {
        AddFileUnit("src/A.cs", "class A;");
        var config = new Config([Config.Default.Lenses[0], Tenancy]);

        var pack = Assert.Single(await RunAsync(config: config));

        Assert.Contains("- lenses: default\n", pack.Markdown);
        Assert.DoesNotContain("### tenancy", pack.Markdown);
        Assert.DoesNotContain(Tenancy.Instructions, pack.Markdown);
    }

    [Fact]
    public async Task Pack_ListsEveryLensThatApplies_OrderedById()
    {
        AddFileUnit("web/api.ts", "export const x = 1;");
        var config = new Config([Tenancy, Config.Default.Lenses[0]]);

        var pack = Assert.Single(await RunAsync(config: config));

        Assert.Contains("- lenses: default, tenancy\n", pack.Markdown);
        Assert.Contains("### default\n\n" + Config.DefaultInstructions + "\n\n### tenancy\n\n" + Tenancy.Instructions + "\n\n## Files\n", pack.Markdown);
    }

    [Fact]
    public async Task RunAsync_ReturnsBatchSizedSetOfDistinctUnits()
    {
        foreach (var name in new[] { "a", "b", "c", "d", "e" })
        {
            AddFileUnit($"src/{name}.cs", $"class {name};");
        }

        var packs = await RunAsync(batch: 3);

        Assert.Equal(3, packs.Count);
        Assert.Equal(3, packs.Select(p => p.UnitId).Distinct().Count());
    }

    [Fact]
    public async Task RunAsync_ReturnsEmpty_WhenEveryUnitIsDone()
    {
        AddFileUnit("src/A.cs", "class A;", UnitStatus.Done);
        AddFileUnit("src/B.cs", "class B;", UnitStatus.Done);

        Assert.Empty(await RunAsync(batch: 5));
    }

    [Fact]
    public async Task Pack_UsesFourBacktickFence_WhenContentContainsThreeBackticks()
    {
        var content = "# Notes\n\n```cs\nvar x = 1;\n```";
        AddFileUnit("docs/notes.md", content);

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("````markdown\n" + content + "\n````\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_RendersSymbolHeading_WhenMemberHasSymbol()
    {
        AddFileUnit("src/A.cs", "class A;", symbol: "A.Run");

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("### src/A.cs :: A.Run (csharp)\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_UsesTextFenceTag_ForUnknownLanguage()
    {
        AddFileUnit("notes.txt", "remember the milk");

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("### notes.txt (unknown)\n\n```text\nremember the milk\n```\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_ContainsResponseSampleVerbatim()
    {
        AddFileUnit("src/A.cs", "class A;");

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("```json\n" + AnalysisResponseJson.Sample + "\n```\n", pack.Markdown);
    }

    [Fact]
    public async Task Pack_OrdersMembersByDistanceThenPath()
    {
        tree.Add("src/Z.cs", "class Z;").Add("src/B.cs", "class B;").Add("src/A.cs", "class A;");
        var members = new List<UnitMember>
        {
            new("slice:1", "src/Z.cs", null, "hz", 1),
            new("slice:1", "src/B.cs", null, "hb", 0),
            new("slice:1", "src/A.cs", null, "ha", 1),
        };
        ledger.Units.Add(new Unit("slice:1", UnitKind.Slice, "GET /x", Fingerprints.Compute(members), UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.AddRange(members);

        var pack = Assert.Single(await RunAsync());

        var headings = pack.Markdown.Split('\n').Where(l => l.StartsWith("### src/", StringComparison.Ordinal)).ToList();
        Assert.Equal(["### src/B.cs (csharp)", "### src/A.cs (csharp)", "### src/Z.cs (csharp)"], headings);
        Assert.Contains("- kind: slice\n", pack.Markdown);
    }

    [Fact]
    public async Task SymbolMember_ShowsOnlyItsLines_NumberedFromTheFile()
    {
        tree.Add("src/Quotes.cs", string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line {i}")));
        const string id = "slice:M:Q.Get";
        AddUnit(id, UnitKind.Slice, "GET /quotes", new UnitMember(id, "src/Quotes.cs", "M:Q.Get", "h", 0, new LineRange(9, 11), "class Q\nvoid Get()"));

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("- key: GET /quotes\n", pack.Markdown);
        Assert.Contains("### src/Quotes.cs :: M:Q.Get (csharp)\n\nlines 9-11\n\n```csharp\n 9 | line 9\n10 | line 10\n11 | line 11\n```\n", pack.Markdown);
        Assert.DoesNotContain("line 8", pack.Markdown);
        Assert.DoesNotContain("line 12", pack.Markdown);
        Assert.DoesNotContain("- outlined:", pack.Markdown);
    }

    [Fact]
    public async Task Budget_KeepsTheEntryPointWhole_AndOutlinesFartherMembersToTheirSignature()
    {
        tree.Add("src/Api.cs", "entry " + new string('x', 400)).Add("src/Svc.cs", "helper body").Add("src/Db.cs", "leaf body");
        const string id = "slice:M:Api.Get";
        AddUnit(id, UnitKind.Slice, "GET /x",
            new UnitMember(id, "src/Api.cs", "M:Api.Get", "h1", 0, new LineRange(1, 1), "class Api\nvoid Get()"),
            new UnitMember(id, "src/Svc.cs", "M:Svc.Run", "h2", 1, new LineRange(1, 1), "class Svc\nvoid Run()"),
            new UnitMember(id, "src/Db.cs", "M:Db.Load", "h3", 2, new LineRange(1, 1), "class Db\nvoid Load()"));

        var pack = Assert.Single(await RunAsync(config: Config.Default with { SliceTokenBudget = 10 }));

        Assert.Contains("- outlined: 2 of 3 members\n", pack.Markdown);
        Assert.Contains("### src/Api.cs :: M:Api.Get (csharp)\n\nline 1\n\n```csharp\n1 | entry xxxx", pack.Markdown);
        Assert.Contains("### src/Svc.cs :: M:Svc.Run (csharp)\n\nline 1, outlined to its signature to fit the token budget\n\n```csharp\nclass Svc\nvoid Run()\n```\n", pack.Markdown);
        Assert.Contains("### src/Db.cs :: M:Db.Load (csharp)\n\nline 1, outlined to its signature to fit the token budget\n\n```csharp\nclass Db\nvoid Load()\n```\n", pack.Markdown);
        Assert.DoesNotContain("helper body", pack.Markdown);
        Assert.DoesNotContain("leaf body", pack.Markdown);
    }

    [Fact]
    public async Task Budget_OutlinesEveryMemberAfterTheFirstOneThatDoesNotFit()
    {
        tree.Add("src/E.cs", "e").Add("src/A.cs", "a body").Add("src/B.cs", new string('b', 200)).Add("src/C.cs", "c");
        const string id = "slice:M:E.Run";
        AddUnit(id, UnitKind.Slice, "GET /e",
            new UnitMember(id, "src/E.cs", "M:E.Run", "he", 0, new LineRange(1, 1), "class E\nvoid Run()"),
            new UnitMember(id, "src/A.cs", "M:A.Run", "ha", 1, new LineRange(1, 1), "class A\nvoid Run()"),
            new UnitMember(id, "src/B.cs", "M:B.Run", "hb", 1, new LineRange(1, 1), "class B\nvoid Run()"),
            new UnitMember(id, "src/C.cs", "M:C.Run", "hc", 2, new LineRange(1, 1), "class C\nvoid Run()"));

        var pack = Assert.Single(await RunAsync(config: Config.Default with { SliceTokenBudget = 10 }));

        Assert.Contains("- outlined: 2 of 4 members\n", pack.Markdown);
        Assert.Contains("```csharp\n1 | a body\n```", pack.Markdown);
        Assert.Contains("### src/B.cs :: M:B.Run (csharp)\n\nline 1, outlined", pack.Markdown);
        Assert.Contains("### src/C.cs :: M:C.Run (csharp)\n\nline 1, outlined", pack.Markdown);
    }

    [Fact]
    public async Task SymbolMember_ShowsTheLinesThatRemain_WhenTheFileShrankSinceTheScan()
    {
        tree.Add("src/A.cs", "line 1\nline 2");
        const string id = "orphan:src/A.cs";
        AddUnit(id, UnitKind.Orphan, "src/A.cs", new UnitMember(id, "src/A.cs", "M:A.Run", "h", 0, new LineRange(2, 5), "class A\nvoid Run()"));

        var pack = Assert.Single(await RunAsync());

        Assert.Contains("lines 2-5\n\n```csharp\n2 | line 2\n```\n", pack.Markdown);
    }
}
