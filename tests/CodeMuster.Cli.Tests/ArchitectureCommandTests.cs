namespace CodeMuster.Cli.Tests;

public class ArchitectureCommandTests
{
    [Fact]
    public async Task MixedRepo_GetsOneUiAndOneApiUnit_WhosePacksShowTheStructure_AndRunCompletesBoth()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();

        var scan = await CliProcess.RunAsync(repo.Root, "scan");
        var status = await CliProcess.RunAsync(repo.Root, "status");
        var ui = await CliProcess.RunAsync(repo.Root, "next", "--kind", "architecture");
        var api = await CliProcess.RunAsync(repo.Root, "next", "--kind", "api");

        Assert.Equal(0, scan.ExitCode);
        Assert.Contains("\narchitecture 0/1\napi 0/1\n", status.Stdout);
        Assert.Contains("- unit: architecture:ui\n", ui.Stdout);
        Assert.Contains("### /customers\n\n- route /customers (web/app/customers/page.tsx:4)\n- heading \"Customers\" (web/app/customers/page.tsx:11)\n", ui.Stdout);
        Assert.Contains("### /quotes\n\n- route /quotes (web/app/quotes/page.tsx:4)\n- heading \"Quotes\" (web/app/quotes/page.tsx:8)\n", ui.Stdout);
        Assert.Contains("### web/app/quotes/page.tsx (typescript)\n\nDefines the UI elements listed above at lines 4, 8.\n", ui.Stdout);
        Assert.Contains("- unit: architecture:api\n", api.Stdout);
        Assert.Contains("- GET /quotes: QuotesController.ListQuotes (src/MixedRepo.Api/Controllers/QuotesController.cs:9)\n", api.Stdout);
        Assert.Contains("  - `[HttpGet(\"quotes/{id}\")] public ActionResult<QuoteSummary> GetQuote(int tenantId, int id)`\n", api.Stdout);

        var runUi = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--kind", "architecture");
        var runApi = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--kind", "api");
        var rescan = await CliProcess.RunAsync(repo.Root, "scan");
        var after = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal((0, 0, 0), (runUi.ExitCode, runApi.ExitCode, rescan.ExitCode));
        Assert.Contains("\narchitecture 1/1\napi 1/1\n", after.Stdout);
    }
}
