using CodeMuster.Infrastructure;

namespace CodeMuster.Cli.Tests;

public class MapReuseCommandTests
{
    private const string Reused = "reused the code map from ";

    // Units, members and the stored map as the scan left them, without the scan's own time.
    private static async Task<string> DumpAsync(string repoRoot)
    {
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), CancellationToken.None);
        var units = await ledger.GetUnitsAsync(CancellationToken.None);
        var members = await ledger.GetMembersAsync(units.Select(u => u.Id).ToList(), CancellationToken.None);
        var map = (await ledger.GetCodeMapAsync(CancellationToken.None))!;
        return string.Join('\n', units.Select(u => u.ToString()))
            + "\n" + string.Join('\n', members.Select(m => m.ToString()))
            + $"\n{map.HeadCommit} {map.InputsDigest} {string.Join(',', map.MappedLanguages)} {string.Join(',', map.FailedLanguages)}\n"
            + Domain.CodeMapJson.Serialize(map.Map);
    }

    [Fact]
    public async Task AnUnchangedRescan_ReusesTheMap_AndLeavesTheLedgerAsAFullRemapDoes_UntilAMappingInputChanges()
    {
        using var repo = TempRepo.FromFixture("mixed-repo");
        repo.CopyRestoredFromFixture("mixed-repo", Path.Combine("web", "node_modules"), "npm ci --prefix fixtures/mixed-repo/web");
        repo.Dotnet("restore", "MixedRepo.sln");
        await CliProcess.RunAsync(repo.Root, "init", "--yes", "--for=none");
        repo.WithoutVulnerabilityScan();

        var first = await CliProcess.RunAsync(repo.Root, "scan");
        var reused = await CliProcess.RunAsync(repo.Root, "scan");
        var afterReuse = await DumpAsync(repo.Root);
        var remapped = await CliProcess.RunAsync(repo.Root, "scan", "--remap");
        var afterRemap = await DumpAsync(repo.Root);

        Assert.Equal((0, 0, 0), (first.ExitCode, reused.ExitCode, remapped.ExitCode));
        Assert.DoesNotContain(Reused, first.Stderr);
        Assert.Contains(Reused + repo.Git("rev-parse", "HEAD").Trim()[..7] + "; nothing mapped changed", reused.Stderr);
        Assert.DoesNotContain("csharp: ", reused.Stderr);
        Assert.DoesNotContain(Reused, remapped.Stderr);
        Assert.Contains("csharp: ", remapped.Stderr);
        Assert.Equal(reused.Stdout, remapped.Stdout);
        Assert.Equal(afterRemap, afterReuse);

        File.AppendAllText(Path.Combine(repo.Root, "web", "tsconfig.json"), "\n");
        var edited = await CliProcess.RunAsync(repo.Root, "scan");

        Assert.Equal(0, edited.ExitCode);
        Assert.DoesNotContain(Reused, edited.Stderr);
        Assert.Contains("csharp: ", edited.Stderr);
    }
}
