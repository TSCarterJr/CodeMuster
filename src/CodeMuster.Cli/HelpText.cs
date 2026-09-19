namespace CodeMuster.Cli;

public static class HelpText
{
    public const string Overview = """
        usage: codemuster <command> [options]
               codemuster help <command>
               codemuster --version

        Audit a Git repository, verify findings, and fix them with your coding agent.

        Get started
          init       Create the repository configuration and ignore the local ledger
          intelligent-config  Use AI to tailor exclusions, lenses and test setup
          doctor     Check Git and the repository's code mappers
          skill      Install instructions for your coding agent

        Audit and review
          scan       Map code and refresh the work queue
          estimate   Estimate input tokens for pending work
          run        Analyze pending units and verify findings
          verify     Run only the verification pass
          status     Show coverage and remaining work
          report     Render findings and coverage as Markdown

        Fix
          fix        Fix confirmed findings and make local commits, one file per worker

        Manual agent workflow
          next       Read the next work pack(s)
          done       Record an agent's response

        Maintenance
          validate   Run the configured final build/test command
          hook       Record an agent change notification
          update     Update CodeMuster itself, or check for an update

        Example
          codemuster init --yes
          codemuster scan
          codemuster estimate
          codemuster run --agent codex -j 4
          codemuster report --out audit.md
          codemuster fix --agent codex -j 4

        Commands use the Git repository containing the current directory.
        Use codemuster <command> --help for options, defaults, and examples.
        """;

    private const string AgentOptions = """
          --agent <name>   Required: claude, codex, gemini, opencode (fake is for tests)
          -j, --jobs N     Run up to N agent calls concurrently (default: 1)
          --attempts N    Maximum attempts per unit, including the first (default: 3)
          --path <path>   Select work under a repo-relative file or folder
          --model <id>    Pass a model to the agent; otherwise use its own default
          --effort <level> Pass an effort level to the agent (unsupported by gemini)

        Before agent work, a preview shows the worker limit, provider, model and thinking level.
        Interactive terminals wait 10 seconds: Enter starts now; Esc or Ctrl+C cancels.
        CI and redirected commands start immediately. Omitted settings use the provider default.
        """;

    public static string? For(string command) => command switch
    {
        "intelligent-config" => """
            usage: codemuster intelligent-config [--agent <name>] [--model <id>] [--effort <level>]

            Inspect tracked repository structure and bounded manifest/source samples using one
            read-only agent call, then apply validated additions to .codemuster/config.json.
            Run codemuster init first. The default agent is codex; claude, gemini and opencode
            are also supported. Model and effort are passed through to the selected agent.

            Adds scoped exclusions and lenses. Selects a detected .NET solution or npm test
            command when test_command is empty. Existing custom settings and nonempty test
            commands are preserved. No tests, audits, repairs or dependency installs are run.
            A unique config.backup-*.json retains the original before atomic replacement.
            Invalid recommendations or a config edited during analysis are not applied.

            The settings preview waits 10 seconds in a terminal: Enter starts now;
            Esc/Ctrl+C cancels. CI and redirected commands do not wait.
            Run codemuster scan afterward to apply the updated audit scope.
            """,
        "init" => """
            usage: codemuster init [--for claude,codex,gemini] [--yes] [--no-gitignore] [--no-hooks] [--no-skills]

            Write .codemuster/config.json and ignore the local ledger in this Git repo.
              --for <agents>   Comma-separated claude,codex,gemini; all or none
              --no-hooks       Install selected skills without change hooks
              --no-skills      Skip agent integration
              --yes            Skip prompts; select all agents unless --for or --no-skills is given
              --no-gitignore   Leave .gitignore unchanged

            Commit the configuration, not .codemuster/ledger.db.
            Next: codemuster doctor, then codemuster scan.
            """,
        "doctor" => """
            usage: codemuster doctor

            Check Git and the C# and TypeScript mappers needed by this repository.
            Prints readiness, diagnostics, and suggested setup commands; installs nothing.
            Works before init. Exit 0 means ready; exit 1 means a check failed.
            """,
        "scan" => """
            usage: codemuster scan [--mode slice|file]

            Refresh the ledger from repository files. Changed units become pending or stale.
              --mode slice   Map entry-point call paths and unreached code (default)
              --mode file    Plan one unit per included file without code mapping

            Also runs dependency audit tools when vulnerabilities is enabled in config.
            Optional dead_code records static candidates; user_experience.enabled queues UI browser reviews.
            Uses no agent calls. Run scan again after changing code or configuration.
            Next: codemuster estimate, then codemuster run --agent codex -j 4.
            """,
        "status" => """
            usage: codemuster status

            Show completed/total units, stale work, exclusions, and mapping fidelity.
            The fix row counts completed file units, including declined findings.
            Coverage measures work recorded; it does not prove the code is bug-free.
            Use report for individual verification verdicts and fix outcomes.
            """,
        "estimate" => """
            usage: codemuster estimate [--path <path>]

            Estimate input tokens for pending units before spending agent calls.
              --path <path>   Select units touching a repo-relative file or folder

            This is an approximate input estimate, not a price or total-token guarantee.
            """,
        "next" => """
            usage: codemuster next [--batch N] [--out <file>] [--path <path>] [--kind <kind>]

            Read pending work as Markdown packs for a manually driven agent session.
              --batch N      Number of packs to read (default: 1)
              --out <file>   Write packs to a file instead of stdout
              --path <path>  Select units touching a repo-relative file or folder
              --kind <kind>  Select file, slice, orphan, verify, or ux work

            Follow each pack's response schema and done command. Reading does not reserve work.
            Do not run independent writers against the same ledger.
            UX needs a browser-capable session and the pack's evidence receipt.
            """,
        "done" => """
            usage: codemuster done <unit> --fingerprint <fp> --findings <file>

            Validate and record the JSON response for a work pack.
            Copy the unit and fingerprint from the pack; --findings names the response file.
            UX validates current source, browser screenshots/hashes, readability, and task-flow checks.
            Next: codemuster next, or codemuster status when no work remains.
            """,
        "run" => "usage: codemuster run --agent <name> [options]\n\n"
            + "Analyze pending units, then verify their findings when verification is enabled.\n\n"
            + AgentOptions + "\n"
            + "  --kind <kind>  Limit work to file, slice, orphan, verify, or ux\n"
            + "  --force        Re-run completed units in the selected scope\n\n"
            + "Repeat the command to resume unfinished work. Use fix to edit code.\n"
            + "Browser work stays incomplete without model calls here; use next --kind ux in a browser-capable session.\n"
            + "Example: codemuster run --agent codex -j 4 --path src\n",
        "verify" => "usage: codemuster verify --agent <name> [options]\n\n"
            + "Recheck current code; record confirmed, refuted, resolved, or unsure. Resolved records fixed with evidence.\n\n"
            + AgentOptions + "\n"
            + "  --force        Re-run completed verification units in the selected scope\n\n"
            + "Equivalent to run --kind verify. UX verdicts require browser evidence through next --kind verify and done.\n"
            + "Static dead-code candidates and UX recommendations are excluded from automatic fix.\n",
        "fix" => "usage: codemuster fix --agent <name> [options]\n\n"
            + "Fix confirmed findings, grouped by file. Creates local commits; never pushes.\n\n"
            + AgentOptions + "\n"
            + "  --stash        Save tracked local edits and restore their staging afterward\n"
            + "  --retry-declined Reopen completed files with declined confirmed findings\n"
            + "  --include-related <files> Comma-separated existing tracked files; requires exact --path and -j 1\n\n"
            + "-j limits file workers: four files with -j 10 use at most four workers.\n"
            + "Parallel workers edit isolated worktrees. Tests, commits, and ledger writes run in sequence.\n"
            + "Configure test_command in .codemuster/config.json to validate each fix before accepting it.\n"
            + "Dirty tracked files prompt for a stash in a terminal; unattended runs need --stash.\n"
            + "Untracked files stay in place. Recovery stashes are retained after restoration.\n"
            + "Ctrl+C keeps completed commits; interrupted workers report recovery paths.\n"
            + "Extra file edits are rejected with their paths and a retained worker checkout.\n"
            + "Repeat fix to retry unfinished files. Then verify current findings and run validate.\n\n"
            + "Example: codemuster fix --agent codex -j 4 --attempts 3\n",
        "validate" => """
            usage: codemuster validate

            Run test_command from .codemuster/config.json against the final repository state.
            Configure it to cover required builds and tests. Missing configuration or a failing
            command exits 1; a successful command exits 0. Runs even when the fix queue is empty.
            """,
        "hook" => """
            usage: codemuster hook

            Used by installed agent hooks after editing or shell tools. Records a tracked-content
            notification in Git metadata without changing the ledger or working tree. Status and
            report warn when content differs from the last completed scan. Prints empty JSON.
            """,
        "report" => """
            usage: codemuster report [--out <file>] [--include-refuted]

            Render findings, verification verdicts, fix outcomes, and coverage as Markdown.
              --out <file>       Write to a file instead of stdout
              --include-refuted  Include findings the verification pass refuted

            Unverified, unsure, and fixed findings remain visible with their recorded state.
            """,
        "skill" => """
            usage: codemuster skill install --for <agent> [--global]

            Install the bundled audit instructions for claude, codex, gemini, or opencode.
              --for <agent>   Required: the harness that will discover the skill
              --global        Install in your home directory instead of this repository

            Run skill install again after an update to refresh an installed copy.
            This installs instructions, not the agent executable or its authentication.
            """,
        "update" => """
            usage: codemuster update [--check]

            Update CodeMuster itself through the npm launcher.
              --check   Show the available version without installing it

            Automatic background checks can be disabled with CI or CODEMUSTER_NO_UPDATE.
            """,
        _ => null,
    };
}
