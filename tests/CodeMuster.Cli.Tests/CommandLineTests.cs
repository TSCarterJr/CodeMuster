namespace CodeMuster.Cli.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_VerbWithOptionsAndPositional()
    {
        var command = CommandLine.Parse(["done", "file:src/A.cs", "--fingerprint", "abc", "--findings", "f.json"]);

        Assert.Equal("done", command.Verb);
        Assert.Equal(["file:src/A.cs"], command.Positionals);
        Assert.Equal("abc", command.Options["fingerprint"]);
        Assert.Equal("f.json", command.Options["findings"]);
    }

    [Fact]
    public void Parse_DoneRefusesAFixUnit_BecauseOnlyFixChecksTheRepair()
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(["done", "fix:src/A.cs", "--fingerprint", "abc", "--findings", "f.json"]));

        Assert.Equal("codemuster done: fix units are recorded by codemuster fix, which applies, tests and commits the repair; run codemuster fix --agent <agent> --path src/A.cs", error.Message);
    }

    [Fact]
    public void Parse_NoArguments_IsAMistake()
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse([]));

        Assert.Equal("usage: codemuster <command> [options]; see codemuster --help", error.Usage);
    }

    [Theory]
    [InlineData("--batch")]
    [InlineData("--batch --out x")]
    [InlineData("--out x --batch")]
    public void Parse_OptionWithoutValue_SaysWhichOptionNeedsOne(string tail)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(["next", .. tail.Split(' ')]));

        Assert.Equal("codemuster next: --batch needs a value", error.Message);
        Assert.Equal("usage: codemuster next [--batch N] [--out <file>] [--path <path>] [--kind <kind>]; see codemuster next --help", error.Usage);
    }

    [Fact]
    public void Parse_KnownFlags_TakeNoValue()
    {
        var command = CommandLine.Parse(["init", "--yes", "--no-gitignore"]);

        Assert.Equal(["no-gitignore", "yes"], command.Flags.Order());
        Assert.Empty(command.Options);
        Assert.Empty(CommandLine.Parse(["init"]).Flags);
    }

    [Fact]
    public void Parse_ShortJobsOption_AndRunFlags()
    {
        var command = CommandLine.Parse(["run", "--agent", "fake", "-j", "4", "--force"]);

        Assert.Equal("4", command.Options["jobs"]);
        Assert.Equal("fake", command.Options["agent"]);
        Assert.Contains("force", command.Flags);
        Assert.Empty(command.Positionals);
        Assert.Equal("codemuster run: -j needs a value", Assert.Throws<UsageException>(() => CommandLine.Parse(["run", "-j"])).Message);
    }

    [Fact]
    public void Parse_LaterOptionWins_AndVerbIsLowercased()
    {
        var command = CommandLine.Parse(["NEXT", "--batch", "1", "--batch", "3"]);

        Assert.Equal("next", command.Verb);
        Assert.Equal("3", command.Options["batch"]);
    }

    [Fact]
    public void Parse_NameEqualsValue_ReadsLikeNameSpaceValue_SplittingAtTheFirstEquals()
    {
        var joined = CommandLine.Parse(["fix", "--agent=fake", "--path=src/a=b.cs", "--include-related=x.cs,y.cs", "--jobs=1", "--model=m=1"]);
        var spaced = CommandLine.Parse(["fix", "--agent", "fake", "--path", "src/a=b.cs", "--include-related", "x.cs,y.cs", "--jobs", "1", "--model", "m=1"]);

        Assert.Equal(spaced.Options.OrderBy(o => o.Key, StringComparer.Ordinal), joined.Options.OrderBy(o => o.Key, StringComparer.Ordinal));
        Assert.Equal("src/a=b.cs", joined.Options["path"]);
        Assert.Equal("2", CommandLine.Parse(["next", "--batch=1", "--batch", "2"]).Options["batch"]);
        Assert.Equal("1", CommandLine.Parse(["next", "--batch", "2", "--batch=1"]).Options["batch"]);
    }

    [Theory]
    [InlineData("run", "codemuster run: --agent is required (claude, codex, gemini, opencode)")]
    [InlineData("verify --path src", "codemuster verify: --agent is required (claude, codex, gemini, opencode)")]
    [InlineData("fix -j 2", "codemuster fix: --agent is required (claude, codex, gemini, opencode)")]
    [InlineData("done u --findings f.json", "codemuster done: --fingerprint is required")]
    [InlineData("done u --fingerprint f", "codemuster done: --findings is required")]
    [InlineData("skill install", "codemuster skill: --for is required (claude, codex, gemini, opencode)")]
    [InlineData("run --agent claude -j 0", "codemuster run: -j must be a positive whole number (got \"0\")")]
    [InlineData("run --agent claude --jobs abc", "codemuster run: --jobs must be a positive whole number (got \"abc\")")]
    [InlineData("fix --agent fake -j -1", "codemuster fix: -j must be a positive whole number (got \"-1\")")]
    [InlineData("fix --agent fake --attempts=0", "codemuster fix: --attempts must be a positive whole number (got \"0\")")]
    [InlineData("next --batch 1.5", "codemuster next: --batch must be a positive whole number (got \"1.5\")")]
    [InlineData("next --kind bogus", "codemuster next: --kind must be one of file, slice, orphan, verify, ux (got \"bogus\")")]
    [InlineData("run --agent fake --kind fix", "codemuster run: --kind must be one of file, slice, orphan, verify, ux (got \"fix\")")]
    [InlineData("scan --mode files", "codemuster scan: --mode must be one of slice, file (got \"files\")")]
    [InlineData("skill install --for gpt5", "codemuster skill: --for must be one of claude, codex, gemini, opencode (got \"gpt5\")")]
    [InlineData("init --no-skills --for claude", "codemuster init: --for cannot be used with --no-skills")]
    [InlineData("init extra", "codemuster init: unexpected argument \"extra\"")]
    [InlineData("report --include-refuted extra", "codemuster report: unexpected argument \"extra\"")]
    [InlineData("done", "codemuster done: missing <unit>")]
    [InlineData("done a b --fingerprint f --findings x", "codemuster done: unexpected argument \"b\"")]
    [InlineData("skill", "codemuster skill: expected \"install\"")]
    [InlineData("skill remove --for claude", "codemuster skill: expected \"install\" (got \"remove\")")]
    [InlineData("run --agent=", "codemuster run: --agent needs a value")]
    [InlineData("run --agent fake --force=yes", "codemuster run: --force takes no value")]
    [InlineData("help run extra", "codemuster help: name one command")]
    [InlineData("update --check", "codemuster update: only the npm launcher can update CodeMuster; install it with npm i -g codemuster")]
    public void Parse_Mistake_IsNamedInOneLine(string arguments, string expected)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(arguments.Split(' ')));

        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData("next --pth web", "codemuster next: unknown option --pth; did you mean --path? (options: --batch, --out, --path, --kind)")]
    [InlineData("next -j 2", "codemuster next: unknown option -j (options: --batch, --out, --path, --kind)")]
    [InlineData("status --since yesterday", "codemuster status: unknown option --since (status takes no options)")]
    [InlineData("status --bogus", "codemuster status: unknown option --bogus (status takes no options)")]
    [InlineData("skill install --for claude --dir x", "codemuster skill: unknown option --dir (options: --for, --global)")]
    [InlineData("done u --fingerprint f --findings x --bogus y", "codemuster done: unknown option --bogus (options: --fingerprint, --findings)")]
    [InlineData("run --agent fake --verbose", "codemuster run: unknown option --verbose (options: --agent, -j/--jobs, --attempts, --path, --model, --effort, --kind, --force)")]
    [InlineData("run --agent fake --bogus x", "codemuster run: unknown option --bogus (options: --agent, -j/--jobs, --attempts, --path, --model, --effort, --kind, --force)")]
    [InlineData("verify --agent fake --kind file", "codemuster verify: unknown option --kind (options: --agent, -j/--jobs, --attempts, --path, --model, --effort, --force)")]
    [InlineData("run --agnet=fake", "codemuster run: unknown option --agnet; did you mean --agent? (options: --agent, -j/--jobs, --attempts, --path, --model, --effort, --kind, --force)")]
    [InlineData("scan --yes", "codemuster scan: unknown option --yes (options: --mode)")]
    [InlineData("doctor --fix now", "codemuster doctor: unknown option --fix (doctor takes no options)")]
    public void Parse_UnknownOption_IsNamed_WithTheValidOnes(string arguments, string expected)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(arguments.Split(' ')));

        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData("stauts", "codemuster: unknown command \"stauts\"; did you mean \"status\"?")]
    [InlineData("STAUTS", "codemuster: unknown command \"STAUTS\"; did you mean \"status\"?")]
    [InlineData("verfy --agent fake", "codemuster: unknown command \"verfy\"; did you mean \"verify\"?")]
    [InlineData("updte", "codemuster: unknown command \"updte\"; did you mean \"update\"?")]
    [InlineData("help stauts", "codemuster: unknown command \"stauts\"; did you mean \"status\"?")]
    [InlineData("frobnicate", "codemuster: unknown command \"frobnicate\"")]
    [InlineData("stat", "codemuster: unknown command \"stat\"")]
    [InlineData("--verbose", "codemuster: unknown command \"--verbose\"")]
    public void Parse_UnknownCommand_SuggestsOnlyACloseOne(string arguments, string expected)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(arguments.Split(' ')));

        Assert.Equal(expected, error.Message);
        Assert.Equal("usage: codemuster <command> [options]; see codemuster --help", error.Usage);
    }

    [Theory]
    [InlineData("status", "usage: codemuster status; see codemuster status --help")]
    [InlineData("run", "usage: codemuster run --agent <name> [options]; see codemuster run --help")]
    [InlineData("skill remove", "usage: codemuster skill install --for <agent> [--global]; see codemuster skill --help")]
    [InlineData("help run extra", "usage: codemuster help <command>; see codemuster --help")]
    public void Parse_Mistake_CarriesTheCommandsUsageLine(string arguments, string expected)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse([.. arguments.Split(' '), "--bogus"]));

        Assert.Equal(expected, error.Usage);
    }

    [Theory]
    [InlineData("next --batch 2 --out p.md --path web --kind slice")]
    [InlineData("next --kind ux")]
    [InlineData("status")]
    [InlineData("done slice:M:A.B() --fingerprint f --findings r.json")]
    [InlineData("skill install --for claude --global")]
    [InlineData("skill install --for opencode")]
    [InlineData("init --for claude,codex --yes --no-gitignore --no-hooks")]
    [InlineData("init --no-skills")]
    [InlineData("intelligent-config --agent claude --model m --effort high")]
    [InlineData("intelligent-config")]
    [InlineData("scan --mode file")]
    [InlineData("scan")]
    [InlineData("estimate --path web")]
    [InlineData("run --agent fake -j 2 --attempts 1 --path web --model m --effort e --kind orphan --force")]
    [InlineData("verify --agent gpt5 --jobs 3 --force")]
    [InlineData("fix --agent fake --stash --retry-declined --allow-failing-tests --include-related a.cs --path b.cs -j 1 --attempts 2 --model m --effort e")]
    [InlineData("report --out a.md --include-refuted")]
    [InlineData("doctor")]
    [InlineData("validate")]
    [InlineData("hook")]
    public void Parse_AcceptsEveryOptionTheCommandReads(string arguments)
    {
        var command = CommandLine.Parse(arguments.Split(' '));

        Assert.Equal(arguments.Split(' ')[0], command.Verb);
    }
}
