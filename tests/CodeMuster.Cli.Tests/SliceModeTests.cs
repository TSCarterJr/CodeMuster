using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class SliceModeTests
{
    [Fact]
    public async Task Scan_BuildsFlowsFromTheFixture_AndTheFakeAgentAuditsEveryUnit()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes");

        var scan = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, scan.ExitCode);
        Assert.Equal("", scan.Stderr);
        Assert.Matches(new Regex(@"^5 slices, 3 orphans, \d+ files, resolution 100\.0%\r?$", RegexOptions.Multiline), scan.Stdout);

        var packs = Path.Combine(repo.Root, "packs.md");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "next", "--batch", "100", "--out", packs)).ExitCode);
        var getQuotes = Assert.Single((await File.ReadAllTextAsync(packs)).Split("# CodeMuster unit\n"), pack => pack.Contains("- key: GET /quotes\n"));
        var handler = getQuotes.IndexOf("return quotes.ListQuotes(tenantId);", StringComparison.Ordinal);
        var service = getQuotes.IndexOf("return repository.ListForTenant(tenantId).Select(ToSummary).ToList();", StringComparison.Ordinal);
        var repository = getQuotes.IndexOf("return _quotes.Where(q => q.Status == \"Open\").ToList();", StringComparison.Ordinal);
        Assert.True(handler > 0 && handler < service && service < repository, getQuotes);
        Assert.Contains("[ApiController] public sealed class QuotesController(IQuoteService quotes) : ControllerBase", getQuotes);
        Assert.DoesNotContain("ArchiveQuote", getQuotes);

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "-j", "4");
        Assert.Equal(0, run.ExitCode);

        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains("\nslice 5/5\norphan 3/3\n", status.Stdout);
        Assert.EndsWith("\ncomplete", status.Stdout.TrimEnd());

        var report = await CliProcess.RunAsync(repo.Root, "report");
        foreach (var key in new[] { "GET /quotes", "GET /quotes/{id}", "ReminderWorker", "/quotes", "/customers", "src/MixedRepo.Api/Services/QuoteService.cs" })
        {
            Assert.Contains($"| {key} | done | fake analysis |", report.Stdout);
        }
    }
}
