using CodeMuster.Application;
using CodeMuster.Infrastructure;

namespace CodeMuster.Cli;

public static class Program
{
    public const string Usage = """
        usage: codemuster <verb> [options]

        verbs:
          scan                                              build or refresh the ledger for this repo
          status                                            print coverage
          next [--batch N] [--out <file>]                   print the next unit pack(s)
          done <unit> --fingerprint <fp> --findings <file>  record the model's response for a unit
          estimate                                          approximate token cost of pending units

        run every verb from the root of the repository.
        """;

    private static readonly string[] Verbs = ["scan", "status", "next", "done", "estimate"];

    public static async Task<int> Main(string[] args)
    {
        var command = CommandLine.Parse(args);
        if (command is null || !Verbs.Contains(command.Verb) || !HasRequiredArguments(command))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        var cancellationToken = cancellation.Token;

        try
        {
            return await RunAsync(command, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(Command command, CancellationToken cancellationToken)
    {
        var repoRoot = await GitSourceTree.FindTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken);
        var config = await new ConfigLoader(new PhysicalFileSystem()).LoadAsync(repoRoot, cancellationToken);
        using var ledger = await SqliteLedger.OpenAsync(Path.Combine(repoRoot, ".codemuster", "ledger.db"), cancellationToken);
        var tree = new GitSourceTree(repoRoot);
        var clock = new SystemClock();

        switch (command.Verb)
        {
            case "scan":
                var scan = await new Scan(ledger, tree, new GitBlobHasher(repoRoot), clock, config).RunAsync(cancellationToken);
                Console.WriteLine($"scanned {scan.FilesIncluded} files ({scan.FilesExcluded} excluded) at {scan.HeadCommit[..7]}: {scan.UnitsCreated} new, {scan.UnitsStale} stale, {scan.UnitsTotal} total units");
                return 0;
            case "status":
                Console.WriteLine((await new Status(ledger).RunAsync(cancellationToken)).Render());
                return 0;
            case "estimate":
                Console.WriteLine((await new Estimate(ledger).RunAsync(cancellationToken)).Render());
                return 0;
            case "next":
                var packs = await new Next(ledger, tree, config).RunAsync(int.Parse(command.Options.GetValueOrDefault("batch", "1")), cancellationToken);
                if (packs.Count == 0)
                {
                    Console.Error.WriteLine("nothing pending; run status");
                    return 0;
                }

                var text = string.Join('\n', packs.Select(pack => pack.Markdown));
                if (command.Options.TryGetValue("out", out var outPath))
                {
                    await File.WriteAllTextAsync(outPath, text, cancellationToken);
                    Console.WriteLine($"wrote {packs.Count} pack(s) to {outPath}");
                }
                else
                {
                    Console.Write(text);
                }

                return 0;
            default:
                var response = await File.ReadAllTextAsync(command.Options["findings"], cancellationToken);
                var done = await new Done(ledger, clock, config).RunAsync(command.Positionals[0], command.Options["fingerprint"], response, cancellationToken);
                Console.WriteLine(done.Message);
                return done.Outcome == DoneOutcome.Recorded ? 0 : 1;
        }
    }

    private static bool HasRequiredArguments(Command command) => command.Verb switch
    {
        "done" => command.Positionals.Count == 1 && command.Options.ContainsKey("fingerprint") && command.Options.ContainsKey("findings"),
        "next" => !command.Options.TryGetValue("batch", out var batch) || (int.TryParse(batch, out var n) && n > 0),
        _ => command.Positionals.Count == 0,
    };
}
