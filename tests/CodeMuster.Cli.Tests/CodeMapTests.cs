using System.Text.RegularExpressions;
using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Cli.Tests;

public class CodeMapTests
{
    private static CodeMap Golden(string mapper) =>
        CodeMapJson.Parse(File.ReadAllText(Path.Combine(TempRepo.FindRepoRoot(), "tests", mapper, "golden", "mixed-repo.json")));

    private static async Task<StoredCodeMap?> StoredMapAsync(string repoRoot)
    {
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), CancellationToken.None);
        return await ledger.GetCodeMapAsync(CancellationToken.None);
    }

    private const string FetchQuotes = "web/lib/api.ts#fetchQuotes";
    private const string ListQuotes = "M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32)";

    private static IEnumerable<string> Edges(IEnumerable<Edge> edges) =>
        edges.Select(edge => $"{edge.From} -> {edge.To} ({edge.Kind})").Order(StringComparer.Ordinal);

    [Fact]
    public async Task Scan_StoresTheMixedRepoMap_EqualToBothMappersGoldens_AtTheScannedCommit()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();
        Assert.Null(await StoredMapAsync(repo.Root));

        var scan = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, scan.ExitCode);
        var stored = await StoredMapAsync(repo.Root);
        Assert.NotNull(stored);
        Assert.Equal(repo.Git("rev-parse", "HEAD").Trim(), stored.HeadCommit);
        Assert.False(stored.IsPartial, string.Join("\n", stored.Map.Diagnostics));
        Assert.Equal([Languages.CSharp, Languages.TypeScript], stored.MappedLanguages);
        var csharp = Golden("CodeMuster.Mapping.CSharp.Tests");
        var typescript = Golden("CodeMuster.Mapping.TypeScript.Tests");
        Assert.Equal(
            csharp.Symbols.Concat(typescript.Symbols).OrderBy(symbol => symbol.Id, StringComparer.Ordinal),
            stored.Map.Symbols.OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
        Assert.Equal(Edges([.. csharp.Edges, .. typescript.Edges, new Edge(FetchQuotes, ListQuotes, EdgeKind.Http)]), Edges(stored.Map.Edges));
        Assert.Equal(
            csharp.EntryPoints.Concat(typescript.EntryPoints).OrderBy(entry => entry.Display, StringComparer.Ordinal),
            stored.Map.EntryPoints.OrderBy(entry => entry.Display, StringComparer.Ordinal));
        Assert.Contains(stored.Map.EntryPoints, entry => entry is { Kind: "http", Display: "GET /quotes" });
        Assert.Contains(stored.Map.Symbols, symbol => symbol.Id == "web/lib/api.ts#fetchQuotes");

        var rescan = await CliProcess.RunAsync(repo.Root, "scan");
        var status = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, rescan.ExitCode);
        Assert.Equal(stored.Map.Symbols.Count, (await StoredMapAsync(repo.Root))!.Map.Symbols.Count);
        Assert.Contains("\nslice 0/5\norphan 0/3\n", status.Stdout);
    }
}

public class HttpLinkTests
{
    [Fact]
    public async Task Scan_LinksFetchQuotesToTheGetQuotesAction_AndRecordsTheUnmatchedCallAndEndpoint_WithStdoutAndUnitsUnchanged()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");
        repo.WithoutVulnerabilityScan();

        var scan = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, scan.ExitCode);
        using (var ledger = await SqliteLedger.OpenAsync(Path.Combine(repo.Root, ".codemuster", "ledger.db"), CancellationToken.None))
        {
            var stored = (await ledger.GetCodeMapAsync(CancellationToken.None))!;
            Assert.Equal(
                [new Edge("web/lib/api.ts#fetchQuotes", "M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32)", EdgeKind.Http)],
                stored.Map.Edges.Where(edge => edge.Kind == EdgeKind.Http));
            Assert.Equal(
                [
                    "http: web/lib/api.ts:14 GET /customers matches no endpoint",
                    "http: GET /quotes/{id} is not called from the mapped UI",
                ],
                stored.Map.Diagnostics);
            Assert.False(stored.IsPartial);
        }

        var stdout = scan.Stdout.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        Assert.Equal(2, stdout.Length);
        Assert.Matches(new Regex(@"^scanned \d+ files \(\d+ excluded\) at [0-9a-f]{7}: \d+ new, 0 stale, \d+ total units$"), stdout[0]);
        Assert.Matches(new Regex(@"^5 slices, 3 orphans, \d+ files, resolution 100\.0%$"), stdout[1]);
        Assert.Single(Regex.Matches(scan.Stderr, "linked "));
        Assert.Contains("linked 1 UI call to an endpoint; 1 call and 1 endpoint unmatched", scan.Stderr);
        Assert.DoesNotContain("warning:", scan.Stderr);
        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains("\nslice 0/5\norphan 0/3\n", status.Stdout);
    }
}
