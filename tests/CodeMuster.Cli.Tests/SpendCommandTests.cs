using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class SpendCommandTests
{
    [Fact]
    public async Task FakeRun_RecordsEveryCall_StatusReportAndEstimateShowSpend_AndConfigPricesTheFakeModel()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes")).ExitCode);
        repo.WithoutVulnerabilityScan();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        Assert.DoesNotContain("spend", (await CliProcess.RunAsync(repo.Root, "status")).Stdout);

        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake")).ExitCode);

        var unpriced = Regex.Match((await CliProcess.RunAsync(repo.Root, "status")).Stdout,
            @"^spend unpriced across (\d+) calls; none had a price when recorded\r?$", RegexOptions.Multiline);
        Assert.True(unpriced.Success);
        var calls = int.Parse(unpriced.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(calls > 10);
        Assert.Contains($"\nNo priced call: {calls} agent call(s), 0 of them failed or rejected; {calls} unpriced.\n",
            (await CliProcess.RunAsync(repo.Root, "report")).Stdout.ReplaceLineEndings("\n"));

        repo.WithPrices("""[{"model": "fake", "input": 3, "output": 15}]""");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--force")).ExitCode);

        var cost = (calls * (1200 * 3m + 300 * 15m) / 1_000_000m).ToString("0.00", CultureInfo.InvariantCulture);
        var status = (await CliProcess.RunAsync(repo.Root, "status")).Stdout.ReplaceLineEndings("\n");
        Assert.Contains($"\nspend ${cost} API-equivalent across {2 * calls} calls; {calls} calls unpriced\n", status);

        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Equal(0, report.ExitCode);
        var text = report.Stdout.ReplaceLineEndings("\n");
        Assert.Contains("\n## Spend\n", text);
        Assert.Contains($"\n| fake | {2 * calls} | {2 * calls * 1200} | {2 * calls * 300} | 0 | 0 | ${cost} + {calls} unpriced |\n", text);
        Assert.Contains($"; priced calls by source: config {calls}\n", text);
        Assert.Contains($"\nunpriced: {calls} call(s); {calls} used a model that had no price when recorded (fake);", text);

        File.AppendAllText(Path.Combine(repo.Root, "web", "lib", "api.ts"), "\nexport const changed = 1;\n");
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        var estimate = (await CliProcess.RunAsync(repo.Root, "estimate")).Stdout.ReplaceLineEndings("\n");
        Assert.Contains("\nAPI-equivalent cost of ~", estimate);
        Assert.Matches(@"\nfake ~\$\d+\.\d+\n", estimate);
        Assert.Contains("\nrecorded cost per call with fake, harness overhead included:\n", estimate);
        Assert.Matches(@"\nfile \d+ call\(s\) at \$0\.0081 each, ", estimate);
    }
}
