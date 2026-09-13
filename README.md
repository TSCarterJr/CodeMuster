# CodeMuster

Audit a codebase with your coding agent and see exactly which work has been completed.

CodeMuster maps C# with Roslyn and TypeScript with the TypeScript compiler, groups code into
work units, and tracks each unit in a local SQLite ledger. Your agent analyzes each unit in a
fresh context. A verification pass tries to refute reported findings. Fix mode groups confirmed
findings by file, runs independent file workers, and creates local commits.

Coverage tells you what was analyzed. It does not prove that every defect was found or that a
fix passes your tests.

## Install and set up

You need Node.js 22 or later and Git. The CLI ships as a self-contained platform build.
C# mapping also needs a suitable .NET SDK; TypeScript mapping needs the target repository's
`typescript` dependency installed. Install and authenticate your chosen coding agent separately.

```sh
npm install -g codemuster
cd your-repo
codemuster init --yes
codemuster doctor
codemuster skill install --for codex
```

Commit `.codemuster/config.json`. Keep `.codemuster/ledger.db` local and ignored.
`doctor` checks the mappers and suggests setup commands; it does not install dependencies.
The skill supports `claude`, `codex`, `gemini`, and `opencode`. Add `--global` to install it for
all repositories, then ask your agent to audit with CodeMuster.

## Drive an audit yourself

```sh
codemuster scan
codemuster estimate
codemuster run --agent codex -j 4
codemuster status
codemuster report --out audit.md
```

`scan` maps entry-point call paths and unreached code by default. Use `scan --mode file` for one
unit per included file. `estimate` approximates pending input tokens. `run` analyzes pending
units and processes verification work when enabled. Repeat `run` to resume unfinished work.

Use `--path src` with `estimate`, `run`, `verify`, or `fix` to work on one part of a repository.
A lens's globs select where its instructions apply; `exclude` removes files from the scan.
The report includes verification verdicts and recorded fix outcomes. Refuted findings are
hidden unless you use `--include-refuted`.

## Fix confirmed findings

First configure the repository's validation command. For example, add this property to the
existing `.codemuster/config.json`:

```json
"test_command": ["dotnet", "test", "YourSolution.sln"]
```

Then run:

```sh
codemuster fix --agent codex -j 4
```

`-j` is an upper limit, with a default of one. Four files with `-j 10` use at most four workers.
Each file's confirmed findings go to one worker. Parallel workers use isolated Git worktrees;
the coordinator applies their completed patches, runs the configured tests, commits each changed
file, and records outcomes in sequence. CodeMuster never pushes the commits.

If tracked files have local edits, the terminal offers to stash them and restore them afterward,
including their staging state. Use `--stash` for unattended consent. Untracked files stay in
place, and recovery stashes are retained. Avoid editing the checkout during a fix run.

A failed attempt is retried up to `--attempts N` times in total (default three). Extra file edits
are rejected and reported with a retained worker path. The untracked Impeccable hook cache is
excluded from the patch and does not block it. Without `test_command`, CodeMuster warns that
it is accepting fixes without running your tests.

Review the commits, then run `scan` and `run` again to audit the changed code. A recorded `fixed`
state means the fix response was accepted; a fresh audit checks whether the finding returns.

## Help and documentation

```sh
codemuster --help
codemuster fix --help
codemuster --version
```

- [User guide and command reference](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/usage.md): configuration, scope, parallelism, retries, and recovery.
- [Contributor guide](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/development.md): build, test, and project layout.
- [Decisions](https://github.com/TSCarterJr/CodeMuster/blob/main/DECISIONS.md) and [implementation checklist](https://github.com/TSCarterJr/CodeMuster/blob/main/MVP-Checklist.md).

These documents describe the repository source; an older installed release may have fewer
options. The npm launcher checks for updates at most once a day and uses a downloaded update on
a later invocation. Set `CI` or `CODEMUSTER_NO_UPDATE` to disable automatic checks.
`codemuster update --check` checks availability without installing; `codemuster update` installs
an available update. Run `skill install` again to refresh an installed skill copy.

## License

MIT
