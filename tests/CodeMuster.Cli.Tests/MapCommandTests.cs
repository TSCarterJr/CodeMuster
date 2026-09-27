using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class MapCommandTests
{
    private static string[] Lines(string output) => output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    [Fact]
    public async Task Map_BeforeAnyScan_Exits2_AndSaysToScan()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");

        var summary = await CliProcess.RunAsync(repo.Root, "map");
        var flow = await CliProcess.RunAsync(repo.Root, "map", "flow", "GET /quotes");

        Assert.Equal(2, summary.ExitCode);
        Assert.Equal(["error: no code map yet; run codemuster scan"], Lines(summary.Stderr));
        Assert.Equal("", summary.Stdout);
        Assert.Equal(2, flow.ExitCode);
        Assert.Equal(["error: no code map yet; run codemuster scan"], Lines(flow.Stderr));
    }

    [Fact]
    public async Task Map_AfterScan_AnswersSummaryFlowCallersMermaidAndHtml_WithoutScanning()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);
        var head = repo.Git("rev-parse", "HEAD").Trim();

        var summary = await CliProcess.RunAsync(repo.Root, "map");
        Assert.Equal(0, summary.ExitCode);
        Assert.Equal("", summary.Stderr);
        var summaryLines = Lines(summary.Stdout);
        Assert.Equal($"code map at {head[..7]}", summaryLines[0][..(12 + 7)]);
        Assert.Contains(summaryLines, line => line.StartsWith("symbols: 19 (csharp 13, typescript 6)", StringComparison.Ordinal));
        Assert.Contains("  GET /quotes (http)  QuotesController.ListQuotes", summaryLines);
        Assert.Contains("  codemuster map flow \"<entry point>\" [--depth N]", summaryLines);

        var flow = await CliProcess.RunAsync(repo.Root, "map", "flow", "GET /quotes");
        Assert.Equal(0, flow.ExitCode);
        var flowLines = Lines(flow.Stdout);
        Assert.StartsWith("flow GET /quotes (http) from QuotesController.ListQuotes", flowLines[0]);
        Assert.Contains(flowLines, line => line.StartsWith("  -> bound  QuoteService.ListQuotes  src/MixedRepo.Api/Services/QuoteService.cs:8", StringComparison.Ordinal));
        Assert.Contains(flowLines, line => line.StartsWith("    -> call  QuoteRepository.ListForTenant  src/MixedRepo.Api/Data/QuoteRepository.cs:17", StringComparison.Ordinal));
        Assert.Contains(flowLines, line => line.StartsWith("      -> call  Money.Format", StringComparison.Ordinal));

        var callers = await CliProcess.RunAsync(repo.Root, "map", "callers", "QuoteRepository.FindForTenant");
        Assert.Equal(0, callers.ExitCode);
        Assert.Contains(Lines(callers.Stdout), line => line.StartsWith("  <- call  QuoteService.GetQuote  src/MixedRepo.Api/Services/QuoteService.cs:13", StringComparison.Ordinal));

        var ambiguous = await CliProcess.RunAsync(repo.Root, "map", "callees", "ListQuotes");
        Assert.Equal(2, ambiguous.ExitCode);
        Assert.StartsWith("error: \"ListQuotes\" matches 3 symbols", ambiguous.Stderr);

        var mermaid = await CliProcess.RunAsync(repo.Root, "map", "flow", "GET /quotes", "--format", "mermaid");
        Assert.Equal(0, mermaid.ExitCode);
        Assert.StartsWith("flowchart TD", mermaid.Stdout);

        var json = await CliProcess.RunAsync(repo.Root, "map", "callees", "QuoteService.ListQuotes", "--format", "json");
        using (var graph = JsonDocument.Parse(json.Stdout))
        {
            Assert.Contains(graph.RootElement.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("name").GetString() == "QuoteRepository.ListForTenant");
        }

        var html = await CliProcess.RunAsync(repo.Root, "map", "flow", "GET /quotes", "--out", "flow.html");
        Assert.Equal(0, html.ExitCode);
        Assert.Equal(["wrote map to flow.html"], Lines(html.Stdout));
        var page = await File.ReadAllTextAsync(Path.Combine(repo.Root, "flow.html"));
        Assert.StartsWith("<!doctype html>", page);
        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*[""']?\s*(https?:)?//", RegexOptions.IgnoreCase), page);
        Assert.DoesNotMatch(new Regex(@"<link\b|@import|url\(\s*[""']?https?:", RegexOptions.IgnoreCase), page);
        Assert.DoesNotContain("http://", page);
        Assert.DoesNotContain("https://", page);
        Assert.Contains("M:MixedRepo.Api.Data.QuoteRepository.ListForTenant(System.Int32)", page);
        Assert.Contains("prefers-color-scheme: dark", page);

        var index = await CliProcess.RunAsync(repo.Root, "map", "--out", "map.html");
        Assert.Equal(0, index.ExitCode);
        Assert.Contains("\"view\":null", await File.ReadAllTextAsync(Path.Combine(repo.Root, "map.html")));
    }
}
