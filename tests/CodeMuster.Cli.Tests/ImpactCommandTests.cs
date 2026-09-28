namespace CodeMuster.Cli.Tests;

public class ImpactCommandTests
{
    private const string RepositoryPath = "src/MixedRepo.Api/Data/QuoteRepository.cs";
    private const string OldBody = "return _quotes.Where(q => q.Status == \"Open\").ToList();";
    private const string NewBody = "return _quotes.Where(q => q.TenantId == tenantId && q.Status == \"Open\").ToList();";

    [Fact]
    public async Task Impact_BeforeAnyScan_Exits2_AndSaysToScan()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");

        var impact = await CliProcess.RunAsync(repo.Root, "impact");

        Assert.Equal(2, impact.ExitCode);
        Assert.Equal("error: no code map yet; run codemuster scan", impact.Stderr.Trim());
    }

    [Fact]
    public async Task ChangingAMethod_PlansAnImpactUnit_WhosePackShowsBothVersionsAndItsCallersUpToTheApiAndPage_AndRunCompletesIt()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "init");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);
        var none = await CliProcess.RunAsync(repo.Root, "impact");
        Assert.Equal(0, none.ExitCode);
        Assert.StartsWith("no impact units from the scan at ", none.Stdout);

        var file = Path.Combine(repo.Root, "src", "MixedRepo.Api", "Data", "QuoteRepository.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace(OldBody, NewBody, StringComparison.Ordinal));
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-am", "scope quotes to the tenant");
        var scan = await CliProcess.RunAsync(repo.Root, "scan");
        var status = await CliProcess.RunAsync(repo.Root, "status");
        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "impact");

        Assert.Equal(0, scan.ExitCode);
        Assert.Contains("\nimpact 0/1\n", status.Stdout);
        Assert.Equal(0, next.ExitCode);
        var pack = next.Stdout;
        Assert.Contains("- unit: impact:M:MixedRepo.Api.Data.QuoteRepository.ListForTenant(System.Int32)\n", pack);
        Assert.Contains("19 |         " + OldBody, pack);
        Assert.Contains("19 |         " + NewBody, pack);
        Assert.Contains("- GET /quotes (http), 2 calls up", pack);
        Assert.Contains("- ReminderWorker (background), 2 calls up", pack);
        Assert.Contains("- /quotes (page), 5 calls up, beyond the depth cap", pack);
        Assert.Contains("### web/lib/api.ts :: web/lib/api.ts#fetchQuotes (typescript)", pack);
        Assert.Contains("### src/MixedRepo.Api/Controllers/QuotesController.cs :: M:MixedRepo.Api.Controllers.QuotesController.ListQuotes(System.Int32) (csharp)", pack);

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--kind", "impact");
        var after = await CliProcess.RunAsync(repo.Root, "status");
        var since = await CliProcess.RunAsync(repo.Root, "impact", "--since", "HEAD~1");
        var listed = await CliProcess.RunAsync(repo.Root, "impact");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\nimpact 1/1\n", after.Stdout);
        Assert.Equal(0, since.ExitCode);
        Assert.StartsWith("1 file changed since HEAD~1; 3 mapped symbols in them", since.Stdout);
        Assert.Contains($"QuoteRepository.ListForTenant  {RepositoryPath}:17  impact unit done", since.Stdout);
        Assert.Contains("  entry points: GET /quotes (http), ReminderWorker (background)", since.Stdout);
        Assert.Contains("  pages: /quotes", since.Stdout);
        Assert.Contains($"QuoteRepository.ListForTenant  {RepositoryPath}:17  done", listed.Stdout);
    }

    [Fact]
    public async Task ChangingAProperty_PlansAnImpactUnitListingItsReaders_AndMapReferencesListsEveryUse()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "init");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);

        var calls = await CliProcess.RunAsync(repo.Root, "map", "references", "IQuoteService.ListQuotes", "--kind", "call");
        var json = await CliProcess.RunAsync(repo.Root, "map", "references", "IQuoteService.ListQuotes", "--format", "json");

        Assert.Equal(0, calls.ExitCode);
        Assert.StartsWith("2 references to IQuoteService.ListQuotes (M:MixedRepo.Api.Services.IQuoteService.ListQuotes(System.Int32)) of kind call\n", calls.Stdout);
        Assert.Contains("  call  src/MixedRepo.Api/Controllers/QuotesController.cs:12:23  in QuotesController.ListQuotes\n", calls.Stdout);
        Assert.Contains("  call  src/MixedRepo.Api/Workers/ReminderWorker.cs:11:31  in ReminderWorker.ExecuteAsync\n", calls.Stdout);
        Assert.Equal(0, json.ExitCode);
        Assert.Contains("\"fromName\": \"ReminderWorker.ExecuteAsync\"", json.Stdout);

        var file = Path.Combine(repo.Root, "src", "MixedRepo.Api", "Data", "Quote.cs");
        File.WriteAllText(file, File.ReadAllText(file).Replace("string Status", "string? Status", StringComparison.Ordinal));
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-am", "status may be missing");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan")).ExitCode);
        var impact = await CliProcess.RunAsync(repo.Root, "impact");
        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "impact", "--batch", "5");
        var reads = await CliProcess.RunAsync(repo.Root, "map", "references", "Quote.Status");

        Assert.Contains("Quote.Status  src/MixedRepo.Api/Data/Quote.cs:3  pending\n", impact.Stdout);
        Assert.Contains("- unit: impact:P:MixedRepo.Api.Data.Quote.Status\n", next.Stdout);
        Assert.Contains($"- read {RepositoryPath}:19:37 in QuoteRepository.ListForTenant\n", next.Stdout);
        Assert.Contains("- read src/MixedRepo.Api/Services/QuoteService.cs:26:92 in QuoteService.ToSummary\n", next.Stdout);
        Assert.Contains($"  read  {RepositoryPath}:19:37  in QuoteRepository.ListForTenant\n", reads.Stdout);

        var unknown = await CliProcess.RunAsync(repo.Root, "map", "references", "NoSuchThing");
        Assert.Equal(2, unknown.ExitCode);
        Assert.StartsWith("error: no symbol or declaration matches \"NoSuchThing\"", unknown.Stderr);
    }
}
