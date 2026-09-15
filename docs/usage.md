# Using CodeMuster

This guide describes the current repository source. Check `codemuster --version` when comparing
it with an installed release. Use `codemuster help <command>` or `<command> --help` for focused
help without initializing a repository.

## The workflow

```text
scan → estimate → run → report → fix → review commits → scan → run
                   └─ verification work, when enabled
```

The ledger remembers completed work between invocations. Scanning discovers code changes and
refreshes the queue; running processes that queue. Scanning alone does not call an AI agent.
Fixing is a separate, explicit command that edits code and creates commits.

### 1. Set up

For AI-driven use, install the [Claude or Codex plugin](distribution.md), start a new session
in the repository, and ask it to audit with CodeMuster. The skill attempts CLI installation
if missing. Plugin users do not also need `skill install`.
Plugin-driven setup uses `init --yes --no-skills` without `--for`, skipping project skills and hooks.
The plugin supplies its own session/edit context hooks. They tell the active agent to follow
the repository's `automation` setting during coding without a separate CodeMuster request.
The integrated project setup described below remains available for standalone skill use.

From a Git repository:

```sh
codemuster init --for codex --yes
codemuster doctor
```

`init` creates `.codemuster/config.json` and adds the ledger to `.gitignore`. Commit the config,
keep the ledger local. `--no-gitignore` leaves `.gitignore` unchanged if you manage it yourself.

Install and authenticate the agent you intend to use: Claude Code, Codex, Gemini CLI, or
OpenCode. `doctor` checks Git and the code mappers, not your provider account. Follow its
suggested restore or dependency-install commands if mapping is not ready.

`init` offers a comma-separated choice of `claude`, `codex`, and `gemini` and installs each
selected project's skill and change hook. `--for all` selects all; `--for none` or `--no-skills`
skips integration. `--yes` skips prompts and selects all unless you specify `--for` or
`--no-skills`. In unattended use without `--yes`, supply `--for` to select agents.
`--no-hooks` installs skills without hooks. Existing settings and other hooks are preserved;
invalid settings stop installation rather than being overwritten. Repeat setup to refresh skills.
Standalone `skill install --for opencode` and `skill install --for codex --global` remain available.

Hooks are registered in `.claude/settings.json`, `.codex/hooks.json`, or `.gemini/settings.json`.
Reload the agent and complete its hook trust/approval prompt when required. They invoke
`codemuster hook` after supported edit and shell tools. This records a tracked-content fingerprint
in worktree-specific Git metadata, without writing the ledger or adding files to a worker patch.
Status/report compare tracked content with the last completed scan and warn about changes;
`scan` acknowledges the content it started with. Read-only commands do not invalidate coverage.
An edit during a scan remains detectable. Untracked files remain outside scan coverage until
tracked. Hooks do not trigger model calls, run scans, or automatically close findings.

Agent configuration references: [Claude hooks](https://code.claude.com/docs/en/hooks),
[Codex hooks](https://learn.chatgpt.com/docs/hooks),
[Gemini hooks](https://geminicli.com/docs/hooks/).

To use a standalone skill instead of the plugin, `codemuster skill install`
accepts `--for claude|codex|gemini|opencode`; add `--global` to install in your home directory.
Repeat installation after updating CodeMuster to refresh the copied instructions.

### 2. Scan and estimate

```sh
codemuster scan
codemuster estimate
```

Default slice mode maps entry points and their call paths. Orphan units cover mapped code not
reached by those slices. File units cover files without mapped symbols or a supported mapper.
Mapping failures may fall back to file coverage with low fidelity; inspect diagnostics and
`status`. `scan --mode file` skips code mapping and plans one unit per included file.

Opt-in `dead_code` assessments also run during scan without model calls. Opt-in
`user_experience` settings add separate UI browser-review units. An orphan is not proof of
dead code, and source coverage does not count as a visual or workflow review. Browser reviews
check whether the primary flow makes sense, plus rendering, button feedback, readability,
text quality and error recovery; technical success alone is insufficient. See
[application reviews](application-reviews.md) for configuration, evidence and repair rules.

With `vulnerabilities` enabled, scanning also invokes dependency audit tools for supported
manifests. These tools may need network access. Their diagnostics are printed separately from
code mapping, and they use no agent calls.

`estimate` uses roughly four bytes per input token. It is not a price quote: responses, retries,
and verification work created by future findings can add to usage.

### 3. Analyze and verify

```sh
codemuster run --agent codex -j 4
codemuster status
codemuster report --out audit.md
```

Each work unit gets a fresh agent call. By default, findings generate verification work that
tries to refute the claim. The verdict is `confirmed`, `refuted`, or `unsure`. To run just that
pass, use `codemuster verify --agent codex -j 4`.

`--model` and `--effort` pass through to the chosen harness. Omitted values use the harness's
own defaults. Gemini does not support the effort option. CodeMuster records the requested agent,
model, and effort as provenance; it cannot prove a provider did not fall back to another model.

`--kind file`, `slice`, `orphan`, or `verify` limits `run` to that kind. `--force` on `run` or
`verify` re-runs completed units in the selected scope. Use it deliberately: it spends calls on
work already recorded.

Browser work uses `next --kind ux` in an active browser-capable agent, followed by the pack's
`done` command and evidence receipt. Headless `run` leaves these units pending without a
model call; it reports the browser requirement while continuing eligible source work.

### 4. Fix and review

Configure a test command before fixing. Then:

```sh
codemuster fix --agent codex -j 4
```

Only confirmed, repair-eligible findings not already marked fixed are selected. Unused-code
candidates and UX recommendations stay report-only. Browser-derived findings also require
current completed UI evidence. Eligible findings in a file go to one worker, even
when different audit units reported them. A worker can address findings or decline them with a
reason. Declining records an outcome; it does not mark the finding fixed.

All fix workers, including the default single worker, edit isolated Git worktrees. The coordinator accepts only the assigned file's
patch, runs the configured tests against accumulated fixes, commits the changed file, and
records outcomes. Tests, commits, and ledger writes are serialized. A response that changes no
file creates no commit. CodeMuster never pushes.

Review local commits with `git log` and `git show`, independently verify the findings, and run
final validation:

```sh
codemuster verify --agent codex --force -j 4
codemuster validate
codemuster report --out audit-after.md
```

A fix record says that the response was accepted and any configured test command passed. It is
not an independent proof that the defect is gone. Resolve remaining confirmed findings and
inspect unsure/declined reasons. Then use `scan` and `run` when refreshed full coverage is needed.
Confirmed or resolved UX verification additionally requires a fresh browser receipt; finish it
with `next --kind verify` and `done` through the active browser host.

## Parallelism and scope

`-j N` (also `--jobs N`) means **up to N concurrent agent calls**, with a default of one. In fix
mode those calls correspond to files: 2,000 findings across 100 files create 100 file work units.
Four files with `-j 10` use no more than four workers. Increase concurrency within your machine's
memory and disk capacity and your agent provider's limits. Full worker checkouts consume disk
space, and serialized tests can become the limiting step.

One CodeMuster process owns the scheduling and ledger writes. Do not start independent `scan`,
`run`, `verify`, `fix`, or manual `done` processes against the same ledger while a run is active.
Use `status`, `report`, and Git inspection to observe progress.

| Setting | Effect |
|---|---|
| `--path src` | Selects queued work touching that repo-relative file or folder. Available on `estimate`, `run`, `verify`, and `fix`. |
| Lens `globs` / `languages` | Select where that lens's instructions apply. They do not exclude files from the repository audit. |
| Config `exclude` | Removes matching files from scanning, in addition to built-in exclusions. Run `scan` after editing it. |

A selected slice may include related code outside the requested folder. `--path` is a work
selection filter, not a boundary on the context needed to understand a call path. Prefer it when
you want to process one area first without changing the repository's audit configuration.

## Configuration

### Automatic use during coding

Set `automation` in `.codemuster/config.json`. The plugin reads the current value each time
its context hook runs, and the skill checks it again before the completion checkpoint:

| Value | Automatic behavior |
|---|---|
| `off` | No automatic CodeMuster work. Explicit CLI/skill requests still work. |
| `update` | Refresh the coverage/work queue with `scan` after changes. No AI review or repair. This is the default when the setting is absent. |
| `review` | Scan, review affected units, verify when configured, and report findings. No CodeMuster repairs. |
| `review_and_fix` | Review affected work, verify candidate findings, repair through `fix`, independently verify repairs, and run configured validation. |

For example, add this field to the existing config without replacing its other settings:

```json
"automation": "review_and_fix"
```

This setting authorizes the selected automatic workflow, so the agent should not ask for the
mode again. Review and fix includes local repair commits; isolated workers need the reviewed
source committed, so a scoped local checkpoint of task-owned changes may be necessary.
Pre-existing work must remain separate. A missing test command, inseparable edits, unavailable
agent, or failed checks remain concrete blockers to report. This mode never authorizes a push.
Explicit instructions for the current task take precedence over automatic settings.

Routine reviews use `next --path <file-or-folder>` and each pack's `done` command, or a scoped
headless `run`, rather than auditing unrelated pending work. New files must be deliberately
tracked before scan can include them. The plugin's hooks supply instructions to the active
agent; they do not perform model calls or mutate the ledger. CodeMuster-owned child processes
set `CODEMUSTER_WORKER=1` so their plugin hooks remain quiet and do not recurse.

Generic review queues exclude repair units; repairs use the dedicated `fix` workflow.
File/folder scope is literal and case-sensitive, including underscores and percent signs.

Invalid automation values are rejected by the CLI. Hook diagnostics also leave automatic
work paused until the settings are corrected; they never guess a more permissive mode.

### Configuration fields

Edit the existing `.codemuster/config.json`; keep lenses that are already useful to your team.

| Key | Default | Meaning |
|---|---|---|
| `lenses` | One `default` lens | Named audit instructions with an `id`, `instructions`, and optional `globs` and `languages`. |
| `automation` | `update` | Automatic plugin workflow: `off`, `update`, `review`, or `review_and_fix`. |
| `slice_token_budget` | `24000` | Approximate amount of full code in a slice before farther members are reduced to signatures. |
| `resolution_threshold` | `0.9` | Required fraction of resolved calls for slice coverage to be considered complete. |
| `verify` | `true` | Create a verification pass for reported findings. |
| `vulnerabilities` | `true` | Run ecosystem dependency audit tools during scanning. |
| `dead_code` | `false` | Record conservative static usage assessments and report-only unused candidates during scan. |
| `user_experience` | Disabled | UI-only browser review settings: `enabled`, optional HTTP(S) `base_url`, and `include`/`exclude` globs. See [application reviews](application-reviews.md). |
| `exclude` | `[]` | Additional repo-relative exclusion globs. |
| `test_command` | `[]` | Program and arguments to run after each fix attempt; empty means no configured validation. |

A glob without a slash matches file names anywhere. Use `vendor/**` to exclude a directory.
Built-in exclusions cover generated files, migrations, lockfiles, and binaries; inspect the
reported excluded-file count when interpreting coverage.

### Validate fixes

`test_command` is an argument array, executed from the repository root. It is not a shell command
string. Add one of these properties to your existing config, using your actual paths:

```json
"test_command": ["dotnet", "test", "YourSolution.sln"]
```

```json
"test_command": ["npm", "--prefix", "web", "test"]
```

For several checks, put them in a repository script and invoke its interpreter:

```json
"test_command": ["node", "scripts/validate-fix.mjs"]
```

Choose a command that terminates, returns nonzero on failure, and covers the affected project.
Install its dependencies beforehand and ensure it passes on the starting code. A failing command
rejects the attempt and triggers a retry. Without one, the CLI prints a warning and accepts fixes
without running your repository's tests.

## Read the results

| Result | Meaning |
|---|---|
| `analyzed N/total` | Units with recorded completed work, across the active unit kinds. |
| `fix N/total` | Completed file fix units; this includes files whose findings were declined. |
| `confirmed` | Verification supported the original finding. It remains the historical verdict after a fix. |
| `fix: fixed` | The finding has an accepted fix outcome and stored reason. |
| `fix: declined` | The agent left the finding unchanged and recorded why. |
| No fix outcome | No fix outcome has been recorded for that finding. |
| `stale` | The unit changed since its recorded analysis and needs another pass. |
| `low-fidelity` | Coverage lacks the normal mapping fidelity; inspect mapping diagnostics. |
| UX not applicable | UX is enabled but no included source files match UI conventions or configured UI includes. |
| Browser evidence incomplete | Applicable UI work remains pending, stale or failed; source analysis does not complete it. |

The report preserves findings and their verification history after fixing. It excludes refuted
code findings by default; use `--include-refuted` to see them. A completed file count is different
from the number of findings fixed. Neither is a count of passing tests.

In `.codemuster/ledger.db`, `findings.fix_status` and `fix_reason` are separate from
`verify_status` and `verify_reason`. Commit messages list addressed IDs under `Findings:` and
declined IDs under `Declined:`. Those IDs can be compared with ledger rows when investigating
recording problems. Inspect a live ledger read-only; do not change its rows during a run.

## Retries, cancellation, and recovery

### A worker fails

`--attempts N` is the total attempts per unit, including the first; the default is three.
Parallel progress prints the attempt number, whether a retry was queued, and when the run gives
up. A queued retry may run after other waiting files. Successful files stay recorded; repeat the
same command to retry unfinished work. Failed fix attempts are stored in the ledger with their
reasons, including test output, and appear under **Failed attempts** in `report`. The next worker
receives the prior failure diagnostic for that unit so it can address the cause. History remains
after recovery; the report labels the current unit status separately. Versions before this
support did not persist every fix failure, and those missing diagnostics cannot be reconstructed.

A finding being verified does not guarantee a worker will produce an acceptable patch. An agent
error, invalid response, out-of-scope edit, or failing test command can reject an attempt.

### A finding was declined

`report --include-refuted` shows verification and fix reasons. A decline remains unresolved even
when its file unit is done. Use the CLI to retry it; already-fixed findings stay excluded:

```sh
codemuster fix --agent codex --retry-declined --path src/file.ts -j 4
```

For repairs involving callers, catalogs, or other related files, inspect which files are necessary
and supply an explicit allowlist. Related-file recovery requires one exact primary file and one
worker; all listed paths must already be tracked:

```sh
codemuster fix --agent codex --retry-declined --path src/file.ts --include-related src/caller.ts,src/catalog.ts -j 1
```

The isolated worker can edit only that group. Tests run against the combined patch, and the
coherent repair is committed together. Other edits still reject the patch. Every unresolved
confirmed finding must receive exactly one addressed/declined outcome before completion.
The AI should not switch to manual repairs merely because a worker declined.

### Verify repairs and finish

```sh
codemuster verify --agent codex --force -j 4
codemuster validate
codemuster report --include-refuted
```

`verify` refreshes checks against current code, including checks retired by scan when the source
changed, while preserving the original finding. `--force` also revisits unchanged completed
checks, including previous refutations; `--path` narrows the scope. Results distinguish confirmed
(still present), refuted (the original claim was wrong), resolved (no longer present, with
resolution evidence), and unsure. A resolved verdict records fixed with its reason. A later
confirmed verdict clears an earlier fixed outcome and reopens its fix unit. Historical analyses
remain stored. Interactive verifiers can submit the same verdict/reason JSON through the
`done` command printed in their pack; agents must not edit database rows themselves.

`validate` runs `test_command` against the final repository state even if there is no fix work.
It exits 1 for a failed command or missing configuration. Configure that command to include the
required builds and tests, using a validation script if several commands are needed. An empty
queue or a done unit does not prove checks passed; declines and unsure findings remain unresolved.
Use `scan` plus `run` afterward when a full audit of changed code is required.

### A worker changes another file

The entire parallel patch is rejected if it contains another tracked file's changes or an
unexpected untracked file. The error names those paths and the retained worker checkout. This
avoids accepting half of a change that depends on another file. Inspect it with:

```sh
git -C "<printed-worker-path>" status --short
git -C "<printed-worker-path>" diff HEAD
```

`git diff` does not include untracked files; inspect the paths listed by `status` too. Do not
apply a rejected patch blindly while other workers are still running. A fix that genuinely needs
several files can be retried with the explicit `--include-related` scope after review.

The specific untracked `.impeccable/hook.cache.json` editor cache is allowed to remain outside
the patch. Other untracked files, and changes to a tracked copy of that cache, still trigger
rejection. Earlier 0.2.3 workers rejected the cache too and removed rejected checkouts without
listing the extra paths.

### Tracked local changes need a stash

On a terminal, `fix` offers to stash tracked edits. Answer `y` to continue; Enter or `n` leaves
them alone. An unattended dirty checkout needs explicit `--stash`:

```sh
codemuster fix --agent codex -j 4 --stash
```

The saved edits and staging state are restored at the end, including cancellation or failure.
Untracked files stay in place. The recovery stash remains available after restoration, and the
CLI prints its identifier. Do not apply it a second time after a successful restoration.

If restoration conflicts, CodeMuster keeps the completed commits and backup and reports the
problem. Inspect `git status` and resolve the conflicts. To reapply later from a clean working
tree, use the exact recovery command printed by the CLI, including `--index` and the saved SHA.

### Stop a run

Press Ctrl+C and let cleanup finish. Completed commits remain. Parallel workers stop before
stash restoration, and interrupted worker worktrees remain at the printed recovery paths.
Uncommitted fix edits in the main checkout are saved separately before original edits are
restored. Avoid force-killing the process during cleanup.

A later `fix` run uses fresh workers for unfinished units; it does not automatically adopt an
interrupted worktree's edits. Retained recovery worktrees consume disk space until you review
and manage them.

## Command reference

| Command | Options |
|---|---|
| `init` | `--for claude,codex,gemini` (or `all`/`none`), `--yes`, `--no-gitignore`, `--no-hooks`, `--no-skills` |
| `doctor` | No options |
| `scan` | `--mode slice` (default), `--mode file` |
| `status` | No options |
| `estimate` | `--path <path>` |
| `run --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--kind file\|slice\|orphan\|verify`, `--force` |
| `verify --agent <name>` | Same as `run`, without `--kind` |
| `fix --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--stash`, `--retry-declined`, `--include-related <files>` |
| `validate` | No options; runs configured final build/tests |
| `hook` | No options; used by installed agent hooks |
| `report` | `--out <file>`, `--include-refuted` |
| `next` | `--batch N`, `--out <file>`, `--path <path>` |
| `done <unit>` | Required `--fingerprint <fp>` and `--findings <json-file>` |
| `skill install` | Required `--for claude\|codex\|gemini\|opencode`, optional `--global` |
| `update` | `--check` to check without installing; handled by the npm launcher |

`--version` prints the version. `--help`, `-h`, and `help` show the overview. Command-specific
help accepts `codemuster help fix` or `codemuster fix --help`.

For manual sessions, `next` prints packs and `done` records responses using the pack's unit ID,
fingerprint, and JSON schema. Reading a pack does not reserve it. Use one coordinator rather than
several independent manual writers.

## Exit codes and updates

`0` means the command completed successfully, not that no findings exist. `1` means a runtime
failure, cancellation, or exhausted retries. `2` generally means invalid usage or missing setup.
`next` with no pending work exits successfully. `doctor` exits `1` when its checks are not ready.

The npm launcher checks for updates at most daily, verifies the downloaded package's integrity,
and uses the new build on a later command. `CI` or `CODEMUSTER_NO_UPDATE` disables automatic
checks. Explicit `update` remains available; `update --check` does not install. Updating the CLI
does not replace the binary already running in another process.


## Recovery, process ownership, and supported limits

The 0.2.7 npm launcher can select an exact binary with `CODEMUSTER_VERSION`. For example,
in PowerShell set `$env:CODEMUSTER_VERSION = '0.2.0'`, then run `codemuster --version`.
In POSIX shells use `CODEMUSTER_VERSION=0.2.0 codemuster --version`. The pin suppresses
background updates and refuses explicit `update`; unavailable versions fail without falling
back. Unset the variable to resume normal version selection. Install the current npm launcher
first: binary self-updates do not upgrade launcher JavaScript. Disabling update checks alone
does not downgrade a binary.

Caches are partitioned by operating system and architecture. Older shared cache entries are
ignored, not deleted. Stop active commands and back up the entire `.codemuster` directory
before a version change. Opening a newer ledger with an older binary is not guaranteed; use a
compatible backup when rolling back. The published 0.2.0 ledger was successfully reopened by
the 0.2.7 candidate with completed coverage preserved; this is not a guarantee for future schemas.

`scan`, `run`, `verify`, `fix`, `done`, and `validate` hold one exclusive coordinator lock in
the common Git directory. A competing command, including in a linked worktree, fails clearly.
The lock is released when the process exits; a leftover lock file need not be deleted. Reading
packs is not a work lease, so manual reviewers still need one owner per assigned unit.

Validation edits outside the selected tracked-file scope stop integration and are preserved
for inspection. A failed commit never records a successful fix. If a commit succeeds but the
ledger write fails, keep the commit, inspect the diagnostic, and run forced verification to
reconcile the finding. Avoid concurrent edits to the checkout during integration.

Fix packs are rendered only when a worker slot is available. Whole-file packs exceeding
`slice_token_budget * 4` characters fail with a path-specific diagnostic before recording
coverage. Increase the budget deliberately or split the file; no silent truncation counts as
analysis. This bounds individual prompt construction, not total repository memory. No
representative maximum-scale benchmark has been completed.

Yarn Classic uses `yarn audit --json`; Yarn 2+ uses `yarn npm audit --all --recursive --json`.
Yarn 4.9.0's real JSON output and command behavior have been exercised. Dependency repairs
that change a manifest and lockfile require both files in the explicit allowed scope.

Claude uses a tool allowlist. Codex disables its shell tool. OpenCode uses a named agent with
read/search permissions, edit permission only for repairs, and denied bash/task tools. Gemini
uses a temporary system tool allowlist and disables extensions and MCP servers for the child.
These are harness controls, not an operating-system security boundary. Trusted harness
configuration, hooks, providers, and repository test commands remain part of the environment.
Claude and Codex completed live repair/test/verification fixtures on Windows; Gemini and
OpenCode currently have automated adapter coverage but no equivalent live acceptance result.
