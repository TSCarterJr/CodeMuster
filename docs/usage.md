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

From a Git repository:

```sh
codemuster init --yes
codemuster doctor
codemuster skill install --for codex
```

`init` creates `.codemuster/config.json` and adds the ledger to `.gitignore`. Commit the config,
keep the ledger local. `--no-gitignore` leaves `.gitignore` unchanged if you manage it yourself.

Install and authenticate the agent you intend to use: Claude Code, Codex, Gemini CLI, or
OpenCode. `doctor` checks Git and the code mappers, not your provider account. Follow its
suggested restore or dependency-install commands if mapping is not ready.

The skill is an alternative way to drive the workflow from an agent session. `skill install`
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

### 4. Fix and review

Configure a test command before fixing. Then:

```sh
codemuster fix --agent codex -j 4
```

Only confirmed findings are eligible. All confirmed findings in a file go to one worker, even
when different audit units reported them. A worker can address findings or decline them with a
reason. Declining records an outcome; it does not mark the finding fixed.

Parallel workers edit isolated Git worktrees. The coordinator accepts only the assigned file's
patch, runs the configured tests against accumulated fixes, commits the changed file, and
records outcomes. Tests, commits, and ledger writes are serialized. A response that changes no
file creates no commit. CodeMuster never pushes.

Review local commits with `git log` and `git show`. Then scan and audit the changed code again:

```sh
codemuster scan
codemuster run --agent codex -j 4
codemuster report --out audit-after.md
```

A fix record says that the response was accepted and any configured test command passed. It is
not an independent proof that the defect is gone. The fresh audit can report a remaining defect.

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

Edit the existing `.codemuster/config.json`; keep lenses that are already useful to your team.

| Key | Default | Meaning |
|---|---|---|
| `lenses` | One `default` lens | Named audit instructions with an `id`, `instructions`, and optional `globs` and `languages`. |
| `slice_token_budget` | `24000` | Approximate amount of full code in a slice before farther members are reduced to signatures. |
| `resolution_threshold` | `0.9` | Required fraction of resolved calls for slice coverage to be considered complete. |
| `verify` | `true` | Create a verification pass for reported findings. |
| `vulnerabilities` | `true` | Run ecosystem dependency audit tools during scanning. |
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

`report --include-refuted` shows verification and code finding fix reasons. A decline remains
unfixed even when its file's fix unit is done, so repeating `fix` alone will skip that completed
unit. Read the reason and inspect the current code. If the correct repair spans callers, shared
catalogs, or tests, your coding agent can make that coherent change directly after the managed
run ends, within the scope you authorized. Validate it, then `scan` and audit again. Do not edit
the ledger to clear a decline; a manual repair does not automatically rewrite its old outcome.

The installed AI skill describes this recovery workflow. Refresh an installed copy after updating
CodeMuster with `codemuster skill install --for codex` (or your harness's name; add `--global` for
a global skill).

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
several files needs coordinated review after the run.

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
| `init` | `--yes`, `--no-gitignore` |
| `doctor` | No options |
| `scan` | `--mode slice` (default), `--mode file` |
| `status` | No options |
| `estimate` | `--path <path>` |
| `run --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--kind file\|slice\|orphan\|verify`, `--force` |
| `verify --agent <name>` | Same as `run`, without `--kind` |
| `fix --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--stash` |
| `report` | `--out <file>`, `--include-refuted` |
| `next` | `--batch N`, `--out <file>` |
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
