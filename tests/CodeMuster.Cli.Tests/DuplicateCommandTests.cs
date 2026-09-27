namespace CodeMuster.Cli.Tests;

public class DuplicateCommandTests
{
    private const string Totals = """
        namespace MixedRepo.Api.Shared;

        public static class Totals
        {
            public static decimal SumOpen(IReadOnlyList<decimal> amounts)
            {
                var total = 0m;
                foreach (var amount in amounts)
                {
                    total += amount;
                }
                return total;
            }

            public static decimal SumArchived(IReadOnlyList<decimal> values)
            {
                var sum = 0m;
                foreach (var value in values)
                {
                    sum += value;
                }
                return sum;
            }
        }

        """;

    [Fact]
    public async Task TwoMethodsWithTheSameNormalizedBody_GetOneDuplicateUnit_ThatListsBothCopies_AndRunCompletesIt()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        File.WriteAllText(Path.Combine(repo.Root, "src", "MixedRepo.Api", "Shared", "Totals.cs"), Totals);
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "totals");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();

        var scan = await CliProcess.RunAsync(repo.Root, "scan");
        var status = await CliProcess.RunAsync(repo.Root, "status");
        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "duplicate");

        Assert.Equal(0, scan.ExitCode);
        Assert.Contains("\nduplicate 0/1\n", status.Stdout);
        Assert.Equal(0, next.ExitCode);
        Assert.Contains("- kind: duplicate\n", next.Stdout);
        Assert.Contains("- key: Totals.SumOpen and 1 other copy\n", next.Stdout);
        Assert.Contains("- src/MixedRepo.Api/Shared/Totals.cs:5-13 Totals.SumOpen", next.Stdout);
        Assert.Contains("- src/MixedRepo.Api/Shared/Totals.cs:15-23 Totals.SumArchived", next.Stdout);
        Assert.Contains("10 |             total += amount;", next.Stdout);
        Assert.Contains("20 |             sum += value;", next.Stdout);

        var run = await CliProcess.RunAsync(repo.Root, "run", "--agent", "fake", "--kind", "duplicate");
        var after = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\nduplicate 1/1\n", after.Stdout);
    }
}
