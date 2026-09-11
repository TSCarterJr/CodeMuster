using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Sets a repo up (D24): writes the default config once and makes sure the ledger is gitignored, touching nothing else.</summary>
public sealed class Init(IFileSystem fileSystem)
{
    /// <summary>The lines <c>init</c> adds to <c>.gitignore</c>.</summary>
    public const string LedgerIgnoreLines = ".codemuster/ledger.db\n.codemuster/ledger.db-*\n";

    /// <summary>Absolute path of the repo's root <c>.gitignore</c>.</summary>
    public static string GitignorePathFor(string repoRoot) => Path.Combine(repoRoot, ".gitignore");

    /// <summary>Runs init; <paramref name="confirmGitignore"/> is asked only when the ledger is not yet ignored.</summary>
    public async Task<InitResult> RunAsync(string repoRoot, Func<CancellationToken, Task<bool>> confirmGitignore, CancellationToken cancellationToken)
    {
        var configPath = ConfigLoader.PathFor(repoRoot);
        var configCreated = !fileSystem.FileExists(configPath);
        if (configCreated)
        {
            await fileSystem.WriteAllTextAsync(configPath, ConfigJson.Serialize(Config.Default) + "\n", cancellationToken);
        }

        var gitignorePath = GitignorePathFor(repoRoot);
        var existing = fileSystem.FileExists(gitignorePath) ? await fileSystem.ReadAllTextAsync(gitignorePath, cancellationToken) : "";
        if (CoversLedger(existing))
        {
            return new InitResult(configCreated, GitignoreOutcome.AlreadyCovered);
        }

        if (!await confirmGitignore(cancellationToken))
        {
            return new InitResult(configCreated, GitignoreOutcome.Skipped);
        }

        var separator = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";
        await fileSystem.WriteAllTextAsync(gitignorePath, existing + separator + LedgerIgnoreLines, cancellationToken);
        return new InitResult(configCreated, GitignoreOutcome.Appended);
    }

    private static bool CoversLedger(string gitignore) =>
        gitignore.Split('\n').Any(line => line.Trim() is ".codemuster/ledger.db" or "/.codemuster/ledger.db");
}
