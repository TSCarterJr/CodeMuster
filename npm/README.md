# CodeMuster

Audit a codebase with Claude Code, Codex, or the standalone CLI, and see exactly which work
has been completed.

CodeMuster maps C# with Roslyn and TypeScript with the TypeScript compiler, groups code into
work units, and tracks each unit in a local SQLite ledger. Your agent analyzes each unit in a
fresh context. A verification pass tries to refute reported findings. Fix mode groups confirmed
findings by file, runs independent file workers, and creates local commits.

Coverage tells you what was analyzed. It does not prove that every defect was found or that a
fix passes your tests.

## Install and set up

### Use through your coding agent (recommended)

Install the CodeMuster plugin from this repository's marketplace. It bundles the same skill
for Claude Code and Codex. Run the commands for your agent:

**Claude Code**

```sh
claude plugin marketplace add TSCarterJr/CodeMuster
claude plugin install codemuster@codemuster
```

**Codex**

```sh
codex plugin marketplace add TSCarterJr/CodeMuster
codex plugin add codemuster@codemuster
```

Start a new agent session in your repository. The plugin directs the agent to use CodeMuster
during coding according to `automation` in `.codemuster/config.json`: `off`, `update`
(default), `review`, or `review_and_fix`. You can also ask for a full audit:

> Audit this repository with CodeMuster.

The skill checks for the CLI, attempts `npm i -g codemuster` if missing, and verifies the
result. This skill requires stable CLI 0.2.7 or later, checks recovery/validation capabilities, and attempts an explicit update once for an older installation. A version pin is respected. If installation is blocked or fails, it explains the problem and gives the manual
command. Plugin use runs `init --yes --no-skills` without `--for` to avoid duplicate project skills; this also
skips project hooks. The plugin supplies its own session/edit context hooks. Review and fix
mode authorizes scoped repairs and local commits; configure `test_command` for validation.
See [automatic use](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/usage.md#automatic-use-during-coding)
for settings, scope, and how existing work is preserved.

This is a repository marketplace; installation does not depend on an official directory
listing. For local-checkout installation, updates, standalone skill downloads, and publication
status, see the [distribution guide](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/distribution.md).

### Use the standalone CLI

You need Node.js 22 or later and Git. The CLI ships as a self-contained platform build.
C# mapping also needs a suitable .NET SDK; TypeScript mapping needs the target repository's
`typescript` dependency installed. These requirements also apply to plugin use, which needs
terminal access to your repository. Install and authenticate your chosen coding agent separately.

```sh
npm install -g codemuster
cd your-repo
codemuster init --for codex --yes
codemuster doctor
```

Commit `.codemuster/config.json`. Keep `.codemuster/ledger.db` local and ignored.
`doctor` checks the mappers and suggests setup commands; it does not install dependencies.
`init` installs project skills and change hooks for your selected agents. Choose a comma-separated
`--for claude,codex,gemini`, or choose interactively with plain `init`. `--yes` selects all unless
`--for` or `--no-skills` is given. Use `--no-gitignore`, `--no-hooks`, or `--no-skills` to opt out.
Reload the agent and approve its hook trust prompt when needed. Hooks track changes without
running an audit. Standalone `skill install` also supports `opencode` and global installation.

### Use the skill without a plugin

With the CLI installed, run `codemuster skill install --for codex`. The installer supports
`claude`, `codex`, `gemini`, and `opencode`; add `--global` for all repositories. Use either
the plugin or a standalone skill copy in a given agent scope to avoid duplicate entries.

## Tailor the configuration with AI

After `init`, run:

```sh
codemuster intelligent-config
# Or choose the agent and model:
codemuster intelligent-config --agent codex --model <model-id> --effort high
```

The command uses one read-only AI call to inspect tracked paths, manifest samples and source
samples. It applies validated exclusions and focused lenses for particular file types, and
selects a detected .NET solution or npm test command when none is configured. Existing custom
settings and test commands are preserved. It reports each applied change and keeps the exact
previous config in `.codemuster/config.backup-*.json`. Run `codemuster scan` afterward.
It does not run the configured tests, install dependencies, or start an audit or repair.

## Drive an audit yourself

```sh
codemuster scan
codemuster estimate
codemuster run --agent codex -j 4
codemuster status
codemuster report --out audit.md
```

Before `run`, `verify`, `fix`, or `intelligent-config` starts agents, a preview shows the worker limit, provider,
model and thinking level. Interactive terminals wait ten seconds: press Enter to start now,
or Escape/Ctrl+C to cancel and change your options. CI and redirected commands do not wait.
Unset model/effort values are labeled provider defaults; pass `--model` and `--effort` to
choose them explicitly.

`scan` maps entry-point call paths and unreached code by default. Use `scan --mode file` for one
unit per included file. `estimate` approximates pending input tokens. `run` analyzes pending
units and processes verification work when enabled. Repeat `run` to resume unfinished work.

Use `--path src` with `next`, `estimate`, `run`, `verify`, or `fix` to work on one part of a repository.
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
Each file's confirmed findings go to one worker. All workers, including the default single worker, use isolated Git worktrees;
the coordinator applies their completed patches, runs the configured tests, commits each changed
file, and records outcomes in sequence. CodeMuster never pushes the commits.

If tracked files have local edits, the terminal offers to stash them and restore them afterward,
including their staging state. Use `--stash` for unattended consent. Untracked files stay in
place, and recovery stashes are retained. Avoid editing the checkout during a fix run.

A failed attempt is retried up to `--attempts N` times in total (default three). Extra file edits
are rejected and reported with a retained worker path. The untracked Impeccable hook cache is
excluded from the patch and does not block it. Without `test_command`, CodeMuster warns that
it is accepting fixes without running your tests.

Retry declines through `fix --retry-declined`. For a repair spanning related files, select an
exact primary `--path` and `--include-related path/to/caller,path/to/catalog -j 1`; all paths
must be existing tracked files. The worker stays inside that explicit scope.

After repairs, run `codemuster verify --agent codex --force` to recheck the known findings against
current code. A `resolved` verdict records the fixed outcome and evidence; a confirmed regression
reopens it. Then run `codemuster validate` to execute your configured build/test command against
the final state, even when no fixes remain. Missing validation configuration is a failure.
Use `scan` and `run` afterward when you need a refreshed audit of all changed code.

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
options. The npm launcher checks availability on every normal command and prints the result to
stderr, with a two-second timeout. Registry failures do not stop your command. Available updates
still install in the background at most daily and take effect on a later invocation. Set `CI`,
`CODEMUSTER_NO_UPDATE`, or an exact version pin to disable automatic checks and installation.
`codemuster update --check` checks availability without installing; `codemuster update` installs
an available update and displays all intervening entries from the packaged changelog. Version
history is maintained in [CHANGELOG.md](https://github.com/TSCarterJr/CodeMuster/blob/main/CHANGELOG.md).
Use `npm install -g codemuster` to upgrade the launcher itself and obtain these new notices;
`codemuster update` upgrades the platform binary. The 0.2.7 launcher supports `CODEMUSTER_VERSION=0.2.0` to select that exact binary, including when a newer build is cached. Unset the variable to resume normal selection; explicit updates are refused while pinned. Install the current launcher first to obtain pin support. Run `skill install` again to refresh an installed skill copy. See the user guide for rollback and ledger precautions.

## License

CodeMuster uses the [Personal and Internal Business Use License](LICENSE).
You may use and privately modify it for personal work and internal company work,
including work on commercial products and client code. You may sell your own
products; you may not sell CodeMuster, commercialize a modified version, or offer
its functionality as a paid service. Redistribution requires written permission.
This is a source-available license with use restrictions. See LICENSE for the full terms.
