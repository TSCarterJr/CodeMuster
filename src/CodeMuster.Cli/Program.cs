namespace CodeMuster.Cli;

public static class Program
{
    public const string Usage = """
        usage: codemuster <verb> [options]

        verbs:
          scan       build or refresh the ledger for this repo
          status     print coverage
          next       print the next unit pack(s)
          done       record findings for a unit
          estimate   approximate token cost of pending units
        """;

    public static int Main(string[] args)
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
