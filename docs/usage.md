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

### The one-command way: `codemuster auto`

At a terminal, `codemuster` on its own (or `codemuster auto`) walks the whole flow. It offers a
newer version first. It then lists the steps (doctor, scan, estimate, run, verify, report, fix,
validate, with fix and validate unticked); type step numbers to toggle them and press Enter.
If a ticked step needs an agent, it asks once for the agent (from those installed), the model, the
thinking level and the jobs. After the estimate it shows the call count and cost and asks before
spending. Each step then runs as its own command, and the first failure stops the rest; running
`auto` again resumes, because every step resumes from the ledger. The steps and agent settings
you choose are remembered for the repository in `~/.codemuster/choices` and offered next time.

Unticking verify leaves the new findings' checks queued (`run --no-verify`) for a later
`codemuster verify`. Fix repairs only confirmed findings, so choosing fix without verify warns
how many are confirmed.

In a script or CI job a bare `codemuster` prints help and never starts work. There, name the steps
and the agent:

```sh
codemuster auto --steps scan,run,report --agent claude -j 4
codemuster auto --yes --skip doctor --agent codex --max-cost 50
```

`--yes` asks nothing (the default steps unless `--steps` or `--skip` say otherwise), and
`--max-cost` stops after the estimate when it is above that many dollars.

`run`, `verify` and `fix` also ask for the agent settings at a terminal when `--agent` is missing,
and print the flags that skip the questions next time. With `--agent` given, nothing is asked:
a missing `--model` or `--effort` uses the provider default and `-j` stays 1. `--yes` also skips
the ten-second start countdown.

### 1. Set up

For AI-driven use, install the [Claude or Codex plugin](distribution.md), start a new session
in the repository, and ask it to audit with CodeMuster. The skill attempts CLI installation
if missing. Plugin users do not also need `skill install`.
Plugin-driven setup uses `init --yes --no-skills` without `--for`, skipping project skills, hooks
and the MCP server registration; the plugin registers the MCP server itself.
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
suggested restore or dependency-install commands if mapping is not ready, or let
`codemuster doctor --fix` run them: `git init` with a first commit (`git add -A`, then
`git commit -m "Initial commit"`) when the folder is not a repository or has no commit yet,
`dotnet restore` for each unrestored solution or project, and the package manager's install
(`npm ci` with a `package-lock.json`, `pnpm install` or `yarn install` with their lockfile when
the tool is on PATH, otherwise `npm install`) where the `typescript` package is missing. With no
`tsconfig.json` or `jsconfig.json` it adds `typescript` as a dev dependency where the mapper
says (`npm i -D typescript`, `pnpm add -D typescript` or `yarn add -D typescript`). On a
terminal it shows each command and asks before running it; `--yes` runs them all; redirected
or CI runs only print them. It then checks again and prints the new result. Without the .NET SDK,
`doctor` says so and links to https://dotnet.microsoft.com/download; it never installs the SDK.

TypeScript mapping uses the compiler API from the `typescript` package installed next to each
`tsconfig.json`. TypeScript 7 has no JavaScript compiler API, so with it, or with no `typescript`
package at all, the mapper uses `@typescript/typescript6` from the same project instead; when that
is missing too, `doctor` names the tsconfig, the version and how to add it. Where the tsconfig's
folder has its own `package-lock.json` that is an npm command such as
`npm i -D @typescript/typescript6 --prefix web`; in a workspace member or a pnpm or Yarn project it
says to add the package with that project's package manager, since `--prefix` on a workspace
member would write a second lockfile there.

The same mapper maps JavaScript (D64). Each `tsconfig.json` or `jsconfig.json` is one program; a
`jsconfig.json` allows JavaScript by default. A repository with neither gets one default program
over its included `.js`, `.jsx`, `.mjs`, `.cjs`, `.ts` and `.tsx` files that reads JavaScript
without type-checking it. JavaScript functions, function-valued top-level consts, class methods and
pages (`pages/` and `app/**/page.jsx`) become symbols and entry points exactly as in TypeScript.
Mapping JavaScript needs Node.js and a `typescript` package, because the mapper uses the
repository's own compiler (D08). Loose scripts that nothing asks to map, such as an ASP.NET site's
`wwwroot/js/site.js` (JavaScript with no `tsconfig.json`, no `jsconfig.json` and no TypeScript
files), are simply not mapped when either is missing: scan and `doctor` print
`javascript: not mapped (no tsconfig, jsconfig or typescript package); files are reviewed whole. Add typescript as a dev dependency to map them`
(or name the missing Node.js), the files stay whole-file units, the map is not partial and
`doctor` stays ready. Once a `tsconfig.json` or `jsconfig.json` or a TypeScript file exists,
mapping was asked for, so a missing compiler fails the map and `doctor` prints the fix:
`run npm i -D typescript` when the repository root (or else the nearest folder above a script
with a `package.json`) has a `package-lock.json`, otherwise
`add typescript as a dev dependency of <folder> with its package manager`.

Express, Koa and Fastify routes become `http` entry points in either language:
`app.get("/users", handler)`, `router.post(...)`, `.put`, `.patch`, `.delete`, `.all` and `.use`
(the last two as `ANY`) on an `express()` app, an `express.Router()`, a Koa `new Router()` or a
`fastify()` instance declared at the top of a file. The route must be a top-level statement with a
literal path; the last argument is the handler. A handler that is a mapped function is the entry
point; an inline handler becomes its own symbol named after the route, such as
`routes/users.js#GET /:id`. `app.use("/api/users", usersRouter)` (or Koa's `router.routes()`)
puts that prefix in front of the router's routes, including routers imported with `require`, and
`:id`, `:id?` and `*rest` are shown as `{id}`, `{id?}` and `{*rest}`, so a route displays as
`GET /api/users/{id}` and the UI-to-API links below match it. Routes registered inside a function
(`function register(app) { app.get(...) }`) or on a Fastify plugin's parameter are not read.

`init` offers a comma-separated choice of `claude`, `codex`, and `gemini` and installs each
selected project's skill and change hook. `--for all` selects all; `--for none` or `--no-skills`
skips integration. `--yes` skips prompts and selects all unless you specify `--for` or
`--no-skills`. In unattended use without `--yes`, supply `--for` to select agents.
`--no-hooks` installs skills without hooks. `init` also registers the codemuster MCP server for
each selected agent (see [Use the map from your agent](#use-the-map-from-your-agent-mcp));
`--no-mcp` skips that. Existing settings, other hooks and other MCP servers are preserved;
invalid settings stop installation rather than being overwritten. Repeat setup to refresh skills.
Standalone `skill install --for opencode` and `skill install --for codex --global` remain available.

Hooks are registered in `.claude/settings.json`, `.codex/hooks.json`, or `.gemini/settings.json`.
Reload the agent and complete its hook trust/approval prompt when required. They invoke
`codemuster hook` after supported edit and shell tools, including Claude Code's PowerShell tool.
This records a change marker in worktree-specific Git metadata, without writing the
ledger or adding files to a worker patch. A hook that cannot record it prints a warning and still
exits 0, so it never fails the agent's tool call. Rerunning `init` updates an existing CodeMuster
hook's matcher in place, raises its timeout to the default if it is lower (a larger timeout you set
is kept), and leaves other hooks alone. Adding or repairing a hook
rewrites that settings file as plain JSON, so its comments and trailing commas are not kept; `init`
names each settings file it changed and says when a skill and hook were already current.

`status`, `report`, `next`, `run`, `fix` and `verify` compare the tracked files scan reads with the last
completed scan and name what changed, for example
`web/lib/index.ts changed since the last scan; run codemuster scan to refresh coverage`.
Those are the files scan reviews, the `.sln`, `.slnx`, `.csproj`, `tsconfig.json` and `jsconfig.json` files the
mappers load, and, while the vulnerability audit is on, each `package.json`.
`scan` acknowledges the content it started with. Read-only commands, staging or committing
unchanged content, other excluded files (lockfiles, documentation, files marked
`linguist-generated`), and untracked files (including `report --out audit.md`, nested
repositories and worktrees) do not count. An edit during a scan remains detectable. When git cannot read a changed file (another program
holds it open, its permissions deny reading, or a required clean filter fails), those commands
print `warning: could not check for changes since the last scan` and still run. Untracked
files remain outside scan coverage until tracked. Hooks do not trigger model calls, run scans, or
automatically close findings.

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

A scan reuses the stored code map instead of mapping again when nothing the mappers read has
changed since that map was taken (D66): the same CodeMuster mapper builds, the same set of mapped
files with the same content, and the same project, solution, MSBuild `.props` and `.targets`,
`global.json`, `NuGet.config`, `tsconfig`/`jsconfig`, `package.json` and lockfile content. It
prints `reused the code map from <commit>; nothing mapped changed`, and the units, fingerprints
and stored map are exactly what mapping again would give. A map that was partial (a mapper
failed or reported a diagnostic, such as an unrestored project) or that skipped a language is
never reused, so restoring or installing and scanning again maps afresh. Mapping also depends on
things outside the repository, such as the installed .NET SDK, restored packages and
`node_modules`; after changing those without changing a tracked file, run `scan --remap` to map
again regardless.

Opt-in `dead_code` assessments also run during scan without model calls. Opt-in
`user_experience` settings add separate UI browser-review units. An orphan is not proof of
dead code, and source coverage does not count as a visual or workflow review. Browser reviews
check whether the primary flow makes sense, plus rendering, button feedback, readability,
text quality and error recovery; technical success alone is insufficient. See
[application reviews](application-reviews.md) for configuration, evidence and repair rules.

With `vulnerabilities` enabled, scanning also invokes dependency audit tools for supported
manifests. These tools may need network access. Their diagnostics are printed separately from
code mapping, and they use no agent calls. When a tool fails, for example offline, on a registry
error or on a failed restore, scan prints a warning naming the manifest and keeps that manifest's
earlier findings; it never records the failure as a clean audit. For a .NET solution, one
project that cannot restore makes the whole `dotnet list package` report count as failed, so
restore every project (or exclude the one that cannot restore) to audit the rest. Each `package.json` folder is
audited once. When it holds lockfiles for more than one tool, the tool named by `packageManager`
in `package.json` is used if its lockfile is there, otherwise pnpm, then Yarn, then npm; a tool
that is not installed is passed over for the next one, and scan prints a warning naming the
lockfiles, the tool that ran and any it passed over.

`estimate` uses roughly four bytes per input token. It is not a price quote: responses and
retries can add to usage. It prices those tokens per model and, once calls are recorded, prices
each pending call at its recorded cost, including the verify calls future findings will create;
see [Spend](#spend).

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

`--kind file`, `slice`, `orphan`, `verify`, `impact`, `duplicate`, `architecture`, or `api` limits
`run` to that kind. `--force` on `run` or
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
| `lenses` | `default` and `simplify` | Named audit instructions with an `id`, `instructions`, and optional `globs` and `languages`. |
| `automation` | `update` | Automatic plugin workflow: `off`, `update`, `review`, or `review_and_fix`. |
| `slice_token_budget` | `24000` | Approximate amount of full code in a slice before farther members are reduced to signatures. |
| `resolution_threshold` | `0.9` | Required fraction of resolved calls for slice coverage to be considered complete. |
| `verify` | `true` | Create a verification pass for reported findings. |
| `batch_units` | `4` | Most small units `run` reviews in one agent call. A unit qualifies when its pack is at most a quarter of `slice_token_budget` and it is on its first attempt; units share a call only with the same lenses and the same folders, and together stay within `slice_token_budget`. Each unit keeps its own analysis and findings, and a unit the reply leaves out runs alone next. The call's usage is split across its units by pack size, so `status` and `report` count one priced row per unit. `1` turns batching off. |
| `verify_batch` | `6` | Most findings of one analysis verified in one agent call. The code is sent once and each finding still gets its own verdict and reason. `1` gives every finding its own call. |
| `vulnerabilities` | `true` | Run ecosystem dependency audit tools during scanning. |
| `dead_code` | `false` | Record conservative static usage assessments and report-only unused candidates during scan. |
| `impact` | `true` | Plan an `impact` unit for each symbol whose body or signature changed since the previous scan. See [Impact review](#impact-review). |
| `duplicates` | `true` | Plan a `duplicate` unit for each group of repeated code. See [Duplicate review](#duplicate-review). |
| `architecture_review` | `true` | Plan one `architecture` unit over the UI's structure and one `api` unit over the HTTP endpoints. See [UI and API design review](#ui-and-api-design-review). |
| `user_experience` | Disabled | UI-only browser review settings: `enabled`, optional HTTP(S) `base_url`, and `include`/`exclude` globs. See [application reviews](application-reviews.md). |
| `exclude` | `[]` | Additional repo-relative exclusion globs. |
| `review_tests` | `false` | Review test files like any other file. By default they are excluded with reason `test`. See [Test files](#test-files). |
| `test_command` | `[]` | Program and arguments to run once before fixing and after each fix attempt; empty means no configured validation. |
| `prices` | Not set | Per-model prices in US dollars per million tokens that override or extend the bundled price table. See [Spend](#spend). |

`init` writes two lenses into a new configuration: `default`, which looks for defects, and
`simplify`, which flags comments that restate the code, commented-out code, stale comments that
contradict the code, and needless complexity such as redundant conditionals, re-implemented
standard library calls, and wrappers that add nothing. It never flags a comment that explains
why. Its findings use category `simplification` and severity `low`; `report` lists them in their
own Simplifications section after the defects, and `fix` repairs them only with
`--include simplification`. An existing configuration is never changed: adding a lens re-audits
every unit, so add the `simplify` lens yourself when you want it.

A glob without a slash matches file names anywhere. Use `vendor/**` to exclude a directory.
Built-in exclusions cover generated files, migrations, lockfiles, binaries, and non-code files.
Matching is case-insensitive for extensions, at any directory depth:

| Category | Examples excluded from AI review |
|---|---|
| Documents | `.md`, `.markdown`, `.txt`, `.rst`, `.adoc`, `.doc`, `.docx`, `.rtf`, `.pdf`, office presentations and spreadsheets; extensionless `README`, `LICENSE`, `NOTICE`, and `CHANGELOG` |
| Data and configuration | `.json`, `.jsonc`, `.json5`, `.jsonl`, `.ndjson`, `.csv`, `.tsv`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.config`, `.log`, project/solution files, `.gitignore`, `.editorconfig`, `.env` and `.env.*` |
| Databases | `.db`, `.db3`, `.sqlite`, `.sqlite3`, `.mdb`, `.accdb`, `.dbf`, and SQLite journal/WAL/shared-memory companions |

Source files, scripts, SQL scripts, HTML, stylesheets and executable templates remain eligible.
Solution/project files, `tsconfig.json` and `jsconfig.json` remain available as mapper inputs without becoming
AI review units. Dependency manifests and lockfiles remain available to ecosystem vulnerability
audits; repository `exclude` globs still apply to those checks.

Run `scan` after upgrading to retire previously queued non-code units. Their history is retained.
Inspect the reported excluded-file count when interpreting coverage.

### Test files

Test code is left out of AI review by default and recorded excluded with reason `test`. A file is
test code when a folder in its path is `test`, `tests`, `__tests__`, `spec`, `specs` or `e2e`, or
starts with `e2e-`; when its name matches `*.test.*`, `*.spec.*`, `*_test.*`, `test_*.py`,
`*Test.cs` or `*Tests.cs`; or when it belongs to a C# project that sets `IsTestProject`, uses the
MSTest SDK, or references `Microsoft.NET.Test.Sdk`, xunit, NUnit or MSTest. Your own `exclude`
globs are checked first.

Test files are still mapped, so `impact` review and `map references` see the calls tests make.
Unused-code review does not count them: product code that only tests call is still a candidate.
`status` shows how many excluded files are tests. Set `"review_tests": true` to review them.

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

The program is looked up only in absolute `PATH` directories, never in the current directory or a
relative entry such as `.` or `node_modules/.bin`, and on Linux and macOS it must be executable.
Git, Node.js, the agents and the audit tools are found the same way, and the programs those start by
name (Roslyn's `dotnet` build host, the `node` an npm shim runs) are not looked for in the
repository either, so a program committed to the repository is not started in their place. That
does not make an untrusted repository safe to scan: C# mapping evaluates its MSBuild files,
TypeScript mapping loads its `typescript` package, and the audit tools honour its settings. Reach a
project-local tool through its package manager, for example `["npx", "jest"]`. On Windows,
CodeMuster sets `NoDefaultCurrentDirectoryInExePath` for itself and everything it starts, so a
script run by `test_command` or by an agent must name a program in the current folder with a path,
for example `["cmd", "/c", ".\\build.cmd"]` rather than `build.cmd`.

Choose a command that terminates, returns nonzero on failure, and covers the affected project.
The command's standard input is empty, so a command that prompts reads end of input at once
instead of waiting.
Install its dependencies beforehand. `fix` runs it once on the unmodified tree before any agent
call: if it fails, its program cannot start, or it changes tracked files, `fix` prints the
command's last lines, restores any stash, and exits 1 without calling the agent. Fix the suite or
the command (`codemuster validate` runs it). `--allow-failing-tests` skips only that run, for a
suite that fails there on purpose until the repair lands; a command whose program cannot start
still stops `fix` before the agent preview. After a repair, a failing command rejects the attempt and
triggers a retry. Without one, the CLI prints a warning and accepts fixes without running your
repository's tests.

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

## Spend

Every agent call that `run`, `verify` and `fix` make is recorded in the ledger, whether its
answer was used or not: a failed call, an unusable answer, a rejected repair and a repair that
finished after the run stopped all cost money too. A call is recorded once the harness has run;
a harness that could not start (not installed, a bad flag) spent nothing and is not recorded.
Each record keeps the input, output, cache-read and cache-write tokens, the model that
answered, the harness's own cost when it reports one, and a cost fixed when the call is recorded.

| Harness | What it reports | Model |
|---|---|---|
| `claude` (`--output-format json`) | Tokens for every model the call used and its own cost | Reported; the model with the largest share |
| `codex` (`exec --json`) | Tokens; input includes cached tokens, which CodeMuster separates | Not reported: the model shown in the start preview |
| `gemini` (`--output-format json`) | Tokens per model; nothing on failure | Reported |
| `opencode` (`run --format json`) | Tokens and cost per step | Not reported: the `--model` passed, if any |

The cost is the harness's own figure when it gives one (Claude Code and OpenCode), and
otherwise the tokens priced at the answering model's rates from a price table bundled with
CodeMuster. The table lists each model's input, output, cache-read, five-minute and one-hour
cache-write price, the official page it came from, and the date it was checked. A model the
table does not know keeps its tokens with no cost and is reported as unpriced, never as free.
A later price change never alters a recorded cost.

All figures are API-equivalent: what the calls would cost at the provider's API prices. When a
harness runs on a subscription, you pay the subscription, not this amount.

- `status` adds one line, such as `spend $12.40 API-equivalent across 214 calls; 3 calls unpriced`.
- `report` adds a Spend section: totals and tokens, a table by model, cost by unit kind and by
  run (each `run`, `verify` or `fix` invocation), the price table date, and why calls are unpriced.
- `estimate` prices the pending input tokens per model: the models already used, any in
  `prices`, else a few representative ones. Output is assumed at 10% of input until 20 calls
  with usage are recorded, then measured from them. Once priced calls exist it also prices the
  pending calls per unit kind for the agent used most recently: at the mean cost of that kind's
  calls once 20 are recorded, else at the mean of all that agent's calls, each line saying which.
  With verification on and at least 20 units analysed, the verify line adds the calls expected
  from the recorded findings per analysed unit. The last line is the total call count and cost.
- `intelligent-config` prints its one call's usage and cost; it does not open the ledger, so that
  call is not in the totals.

Harness overhead dominates small units. Each Claude Code call carries about 45K tokens of its
own system prompt and tools before any of the pack. The first call writes them to the prompt
cache at the one-hour rate (2x input), about $0.36 on Claude Opus 5.5, and calls within the
cache lifetime read them back at the much lower cache-read rate. The pack-based token estimate
does not include this overhead; the recorded cost per call does, which is why the two totals
can differ by more than ten times.

To price a model the table lacks, or to use your own negotiated rates, add `prices` to
`.codemuster/config.json`. Rates are US dollars per million tokens; a missing cache rate is
charged at the input rate. An entry matches its model id exactly, and also the same id with a
release date or a bracketed context tag after it, such as `claude-haiku-4-5-20251001`.

```json
"prices": [
  {"model": "in-house-7", "input": 1.0, "output": 5.0, "cache_read": 0.1, "cache_write": 1.25},
  {"model": "claude-opus-5-5", "input": 4, "output": 20, "cache_read": 0.2, "cache_write": 5, "cache_write_1h": 8}
]
```

Config prices apply to calls recorded after the change, and win over the bundled table.

## Code map

`scan` stores the call graph it mapped (D60). `map` reads that stored map and never scans (D62),
so it answers immediately and reflects the commit of the last mapping scan. Before any scan it
exits 2 with `error: no code map yet; run codemuster scan`.

```sh
codemuster map
codemuster map flow "GET /quotes"
codemuster map callers QuoteRepository.FindForTenant --depth 3
codemuster map callees QuoteService.ListQuotes --format json
codemuster map flow "GET /quotes" --format mermaid --out quotes.md
codemuster map --out map.html
codemuster map references Quote.Status --kind write
```

- `map` prints the commit and scan time, symbol counts by language, edge counts by kind, the
  entry points, the UI-to-API results (calls that match no endpoint, calls whose URL is built at
  runtime, and endpoints the UI never calls), a warning when a mapper failed (the map is
  partial), and the other forms.
  `--format json` prints the same as JSON.
- `map callers <symbol>` and `map callees <symbol>` list the direct callers or callees, each
  with its edge kind (`call`, `bound`, `implements`, `overrides`, or `http` for a UI call that
  reaches an API endpoint), `path:line` and signature.
  `--depth N` (default 1) walks further and prints the tree.
- `map flow <entry point>` prints the call tree from an entry point, matched by its display
  ignoring case (`"GET /quotes"`, `/quotes`, or `quotes` without the slash) or by its symbol id;
  `--depth` defaults to 6. A page's flow follows `http` edges into the API, so
  `map flow quotes` runs from the React page through its `fetch` call to the controller,
  service and repository. In Git Bash on Windows write page routes without the leading slash:
  Git Bash rewrites `/quotes` into a Windows path before CodeMuster sees it.
- `map references <symbol>` lists every recorded use of a method, function, type, field,
  property, constant or interface member (D72, D76): calls, reads, writes, type uses,
  inheritance and implementation, attributes, imports, and for an endpoint the UI calls that
  reach it (`http`), each with `path:line:column` and the symbol it sits in. It prints what the
  MCP `references` tool answers, up to 1,000 uses, and names files changed since the scan.
  `--kind <kind>` keeps one kind; `--format json` prints the tool's structured answer.
  Reflection, `dynamic` and string lookups are not resolved.

`<symbol>` is an exact symbol id, `Type.Method` (`QuoteService.ListQuotes`), a method or function
name (`ListForTenant`), or part of one. When several symbols match, `map` lists them with their
ids and exits 2; rerun it with a more exact name or one of the ids.

`--format mermaid` prints a `flowchart TD` that GitHub and most documentation tools render; node
ids are stable (`n0`, `n1`, ...), edges are labelled with their kind, and a flow's entry point is
drawn as its own node. `--format json` prints `{nodes: [{id, name, path, line, kind, container}],
edges: [{from, to, kind}], truncated}` plus the root, direction and depth.

`--out <file>` writes to a file instead of stdout. A file ending in `.html` becomes one
self-contained page with the map embedded and its drawing code written into the page: no
external scripts, fonts or network access, so it works offline and can be attached anywhere. It
draws the graph left to right by depth, searches symbols and entry points, shows a node's
`path:line`, signature, callers and callees on click, and redraws from a node on double-click.
It follows the system's light or dark setting. `map --out map.html` without a subcommand lists
the entry points to pick from.

Walks visit each symbol once, so cycles and recursion are shown once (`(see above)` in text).
They stop at the depth and at 300 nodes, and every format says how many reachable nodes were
left out: `truncated: N more nodes; use --depth or a narrower start`.

## Use the map from your agent (MCP)

`codemuster mcp` runs a read-only Model Context Protocol server over standard input and output
(D73), so an agent can ask the stored map the questions a language server answers: who calls
`login`, from which file, line and method, and what a change reaches. It speaks JSON-RPC 2.0, one
message per line, and serves clients that open with `initialize` (protocol 2025-11-25 or
2025-06-18) and clients that send the protocol version with every request (2026-07-28). Standard
output carries only protocol messages; anything else goes to standard error. It never writes the
ledger, calls a model or changes files, and it does not take the lock other commands use.

The Claude and Codex plugin registers the server, and `init` adds it for the agents it sets up
(`--no-mcp` skips it): `.mcp.json` at the repository root for Claude Code, `[mcp_servers.codemuster]`
in `.codex/config.toml` for Codex (read in trusted projects), and `mcpServers` in
`.gemini/settings.json` for Gemini CLI. A server already named `codemuster` is left as it is, and
other servers and settings are kept. Claude Code asks before it uses a project's `.mcp.json`
server; approve it once. Every registration starts the server the same way:

```json
{"mcpServers": {"codemuster": {"command": "node", "args": ["-e",
  "process.exitCode=require(\"node:child_process\").spawnSync(\"codemuster mcp\",{stdio:\"inherit\",shell:true}).status??1"]}}}
```

npm installs `codemuster` as a `.cmd` script on Windows, which agents cannot start without a
shell, so `node` starts `codemuster mcp` through the shell on every platform and passes standard
input and output through. Where `codemuster` is a real executable on your PATH you can register
`{"command": "codemuster", "args": ["mcp"]}` instead; add `"--refresh"` to scan first when the map is
stale.

| Tool | Answers |
|---|---|
| `find_symbol {query, kind?, limit?}` | Symbols and declarations matching a name, `Type.Member`, id or path fragment, with id, kind, `path:line`, signature and container |
| `references {symbol, kind?, limit?}` | Every reference with its kind (`call`, `read`, `write`, `type`, `inherit`, `implement`, `attribute`, `import`), path, line, column and containing symbol; for an endpoint also the UI calls that reach it (`http`) |
| `callers {symbol, depth?}`, `callees {symbol, depth?}` | The call edges up or down (depth 1 by default, at most 6), with each call site's line and column when the map records references |
| `call_path {from?, to}` | The shortest call path from each entry point that reaches the symbol, or from `from` |
| `impact {symbol}` | Callers up to their entry points and pages, callees, and the readers, writers and type users with the entry points they reach |
| `http_links {endpoint? \| symbol?}` | The UI functions that call an endpoint or the endpoints a UI function calls, plus UI calls that match no endpoint |
| `entry_points {kind?}` | HTTP endpoints, background services and pages with their handlers |
| `duplicates {symbol?}` | Groups of symbols whose bodies match apart from names and literals |

A symbol is given as `find_symbol` returns it: an exact id, `Type.Member`, a bare name or part of
one. A name that matches several symbols returns an error listing them with their ids. Every
result has a short text for the agent and the same answer as structured JSON, with the commit and
time of the scan it came from, how many results a cap left out, and `stale`: the files it cites
whose content changed since that scan (their lines may have moved; run `codemuster scan`).
References come from ledger schema 9; a map stored before it, or a language whose mapper does not
record references, is named in the result rather than shown as "no references".

The server loads the map on the first call and loads it again after any later `scan`, so a scan
in another terminal is picked up by the next call. Before the first scan every tool answers with
an error telling the agent to run `codemuster scan`, and in a folder without `codemuster init` it
says to run `init`. With `--refresh`, a call first runs a scan when files changed since the map
(the scan takes the coordinator lock; when another CodeMuster command holds it the refresh is
skipped and the result says so).

## Impact review

Slices go stale when code they hold changes, but they are then reviewed cold. An impact unit asks
the targeted question instead (D67). When a `scan` finds a symbol whose body or signature changed
since the map the previous scan stored, it plans one `impact` unit for it (id
`impact:<symbol id>`). A declaration without a body counts too (D72): a type whose header
changed, or a field, property, constant, enum member or interface member whose declaration
changed. New and deleted symbols are not impact targets; their files' own units cover them. The
unit holds:

- the symbol's previous text, read from Git at the commit the previous scan mapped (if that scan
  mapped uncommitted edits, the committed text may differ, and the pack says so), and its
  current text;
- its callers and the code that reads, writes or names it, walked up every edge kind including
  the `http` edges from UI calls to endpoints and every recorded reference, at most 4 levels up
  and 40 symbols, each with its body so the use sites are visible;
- each recorded use of it (up to 100) with its kind and `path:line:column`;
- its direct callees, shown as signatures;
- the entry points and UI pages that reach it, however far up, and what the caps left out.

The agent answers one question: does anything upstream or downstream now break or misuse the
change (changed return values or meaning, new exceptions or nulls, changed parameters, routes the
UI still calls, callees now used incorrectly)? Findings use the usual schema with lens
`impact`, cite lines in the changed symbol or a caller, get a verify unit, and can be fixed.

An impact unit waits until it is analyzed. A later scan that finds its symbol unchanged keeps it
pending, and if the symbol changes again before the review, the unit keeps the version it was
first planned against, so the pack shows everything that changed since the last review. Once
analyzed it stays done; when its symbol changes again it goes stale and is compared with the
newer map, and a scan that finds a done unit's symbol unchanged retires it. A unit whose symbol
was deleted is retired. `"impact": false` in `.codemuster/config.json` turns impact units off. Run only them with
`codemuster run --agent <name> --kind impact`.

`codemuster impact` reads the stored map and never scans or calls a model. It lists the impact
units from the last scan with their status, callers, entry points and pages. With
`--since <ref>` it lists every mapped symbol in the files `git diff --name-only <ref> HEAD`
reports, the same way, marking the ones with an impact unit; uncommitted edits are not listed.
`--format json` prints the same data. Before any scan it exits 2.

```sh
codemuster impact
codemuster impact --since main --format json
```

## Duplicate review

Both mappers record a normalized body hash for every method and function: the code with every
identifier and literal replaced, so copies that differ only in names or values match (D68).
Each `scan` plans one `duplicate` unit per group of at least two symbols with the same normalized
hash, in one language, each at least 6 lines long and in an included file (so excluded and
generated files never count). Copies in C# and TypeScript never group together. The unit id is
`duplicate:<normalized hash>`; when one hash has groups in two languages each id also names its
language. A unit holds at most 12 copies, the first by path; the pack says how many there are in
all when it is capped.

The pack shows every copy with its `path:lines` and body and asks whether they should be
consolidated, where the shared version should live and what it would look like. Findings use
category `simplification` and severity `low`, one per copy, so `report` lists them under
Simplifications and `fix` repairs them only with `--include simplification`. Duplication that is
appropriate (tests, generated DTOs, deliberately separate domains) gets no finding. A unit
reruns when a copy it holds changes or its set of copies changes, and is retired when the group
disappears. `"duplicates": false` turns duplicate units off; run only them with
`codemuster run --agent <name> --kind duplicate`.

## UI and API design review

Some design problems only show across screens or across endpoints, where a per-screen browser
review (D48) or a per-slice audit cannot see them (D69). Each `scan` plans at most two units for
that, one agent call each:

- `architecture` (id `architecture:ui`), when the TypeScript mapper read any UI structure from an
  included file: every page route, navigation or menu entry, section heading, and form control or
  setting with its label. The pack shows that structure as a tree, one group per page route and
  one per file without a route, each element nested under the heading it sits in and followed by
  its `path:line`; it shows no code. The agent looks for misplaced or duplicated settings and
  actions (such as an "Auto charge customer" switch under General while a Payments section
  exists), inconsistent names, orphan pages no navigation reaches, buried features, and
  destructive actions next to safe ones.
- `api` (id `architecture:api`), when there is at least one `http` entry point in an included
  file: every endpoint's method and route, handler, and signature with its attributes (such as
  `[Authorize]` and `[AllowAnonymous]` on the class or the action), grouped by the first segment
  of the route, each with its `path:line`. The agent looks for inconsistent naming or
  pluralization, verbs that do not match what the handler does, inconsistent error shapes or
  pagination, missing authorization within a group, and duplicate endpoints.

Their members are the files that define the elements or endpoints, and each file's member hash
also covers the structure read from it, so a unit reruns only when one of those files or its
structure changes. Findings cite the file and line of the control or endpoint (categories
`architecture` and `api`), get verify units, and can be fixed like any other. The ledger stores
the UI structure with the code map so the pack can be built after the scan.
`"architecture_review": false` turns both off; run only them with
`codemuster run --agent <name> --kind architecture` or `--kind api`.

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
error, invalid response, out-of-scope edit, or failing test command can reject an attempt. So can
an answer that marks findings addressed while the file is unchanged: the retry is told to edit the
file or decline each finding with a reason, so every finding `fix` records as fixed has a commit.

### A file was skipped as too large

A whole-file repair pack above `slice_token_budget` is skipped with its reason rather than failing
the run: the remaining files are still fixed and committed, the skip spends no attempt, and the
exit code stays zero. `status` and `report` show the skip and never count it as repaired. Raise
`slice_token_budget` in `.codemuster/config.json` or split the file, and the next `fix` re-checks
the size and picks it up without a flag or a rescan.

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
The AI should not switch to manual repairs merely because a worker declined. A finding the audit
confirms in an already-repaired file after that repair finished is picked up by the next `fix`
without a flag; only declines need `--retry-declined`.

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
CLI prints its identifier. Do not apply it a second time after a successful restoration. Tracked
changes still in the working tree when restoration starts (output `test_command` wrote, or an
interrupted repair) are saved first in a second stash, and the CLI names that one too.

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
| `init` | `--for claude,codex,gemini` (or `all`/`none`), `--yes`, `--no-gitignore`, `--no-hooks`, `--no-mcp`, `--no-skills` |
| `doctor` | `--fix` to offer the setup commands it found, `--yes` to run them without asking |
| `scan` | `--mode slice` (default), `--mode file`, `--remap` (map again even when nothing it reads changed) |
| `status` | No options |
| `estimate` | `--path <path>` |
| `run --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--kind file\|slice\|orphan\|verify\|ux\|impact\|duplicate\|architecture\|api`, `--force` |
| `verify --agent <name>` | Same as `run`, without `--kind` |
| `fix --agent <name>` | `-j N`, `--attempts N`, `--path <path>`, `--model <id>`, `--effort <level>`, `--stash`, `--retry-declined`, `--allow-failing-tests`, `--include-related <files>`, `--include simplification` |
| `validate` | No options; runs configured final build/tests |
| `hook` | No options; used by installed agent hooks |
| `report` | `--out <file>`, `--include-refuted` |
| `map` | `callers <symbol>`, `callees <symbol>`, `flow <entry point>` or `references <symbol>`; `--depth N`, `--kind <kind>` (references), `--format text\|mermaid\|json`, `--out <file>` (`.html` writes an interactive page) |
| `impact` | `--since <ref>`, `--format text\|json` |
| `mcp` | `--refresh` to scan first when the map is stale; serves MCP over standard input and output |
| `next` | `--batch N`, `--out <file>`, `--path <path>`, `--kind file\|slice\|orphan\|verify\|ux\|impact\|duplicate\|architecture\|api` |
| `done <unit>` | Required `--fingerprint <fp>` and `--findings <json-file>` |
| `skill install` | Required `--for claude\|codex\|gemini\|opencode`, optional `--global` |
| `update` | `--check` to check without installing; handled by the npm launcher |
| `intelligent-config` | `--agent` (default codex), `--model`, `--effort`; apply AI-recommended config additions after init |

`--version` prints the version. `--help`, `-h`, and `help` show the overview. Command-specific
help accepts `codemuster help fix` or `codemuster fix --help`.

Options take `--name value` or `--name=value`, and `-j` also takes `-j4` or `-j=4`; when an option
is repeated, the last one wins. `init --for` takes `all`, `none`, or a comma-separated list of
`claude`, `codex` and `gemini`.
Each command accepts only the options listed above, so a mistyped option is rejected rather than
ignored. A usage mistake prints one line naming it, followed by the command's usage line, and
exits 2:

```text
$ codemuster run
codemuster run: --agent is required (claude, codex, gemini, opencode)
usage: codemuster run --agent <name> [options]; see codemuster run --help

$ codemuster next --pth web
codemuster next: unknown option --pth; did you mean --path? (options: --batch, --out, --path, --kind)
usage: codemuster next [--batch N] [--out <file>] [--path <path>] [--kind <kind>]; see codemuster next --help

$ codemuster stauts
codemuster: unknown command "stauts"; did you mean "status"?
usage: codemuster <command> [options]; see codemuster --help
```

Running `codemuster` with no arguments prints the overview to stderr and exits 2. Errors found
while a command runs start with `error:`, as in
`error: unknown agent 'gpt'; choose one of claude, codex, gemini, opencode`.

For manual sessions, `next` prints packs and `done` records responses using the pack's unit ID,
fingerprint, and JSON schema. Reading a pack does not reserve it. Use one coordinator rather than
several independent manual writers.

## Intelligent configuration

`codemuster intelligent-config` uses Codex by default. Select another supported harness with
`--agent claude`, `--agent gemini`, or `--agent opencode`; `--model` and `--effort` use the same
pass-through settings as audit commands (Gemini does not support effort). Run `init` first.
The command shows the agent settings and the same interactive countdown before its one AI call.

The AI receives the current configuration, tracked-file inventory and language counts, plus
bounded manifest and representative source samples. It is configuration discovery, not an
exhaustive source audit. Inventory and sample truncation are identified in the context.
Document/data files and unrecognized formats such as `.env` and `.pem` are not sampled as
source; selected build/package manifests provide setup evidence. No untracked files are scanned.

Validated recommendations are applied immediately:

- Add exclusions for evidenced generated/vendor/build output. Patterns must match tracked paths;
  a recommendation excluding all remaining source is rejected.
- Add focused lenses using the existing `globs`, `languages` and `instructions` fields. Existing
  lens definitions are preserved, and new lenses must match included source.
- Fill an empty `test_command` from detected `.sln`/`.slnx` solutions or `package.json` test scripts.
  Existing commands are preserved. Discovery configures a command; it does not run it.

The command preserves all other settings, including automation permissions, verification,
vulnerability checks, budgets and optional review toggles, as well as unknown config properties.
It prints the changes and their reasons. Before replacement, the exact original config is saved
as `.codemuster/config.backup-<UTC timestamp>.json` (with a suffix when necessary). A changed config
is written atomically. Invalid responses, cancellation before application, or config edits made
during analysis prevent application. The ledger is not opened or changed.

Review the resulting config diff and run `codemuster scan` to refresh the audit scope. To undo a
configuration change, copy the reported backup over `.codemuster/config.json`, then rescan.
Use `codemuster validate` separately when you want to execute the configured test command.

## Exit codes and updates

`0` means the command completed successfully, not that no findings exist. `1` means a runtime
failure, cancellation, or exhausted retries. `2` generally means invalid usage or missing setup.
`next` with no pending work exits successfully. `doctor` exits `1` when its checks are not ready.

The npm launcher checks update availability on every normal invocation, including `scan`,
`init`, `verify`, `report`, and `--version`. It prints the installed/latest version status to
stderr so command stdout remains usable. The check has a two-second timeout; offline, timed-out
or invalid registry responses print that the check was unavailable and let your command continue.
It never claims you are up to date when the check fails. `hook`, which installed agent hooks run
after every edit, `mcp`, which agents start as their MCP server, and commands run inside a
CodeMuster worker (`CODEMUSTER_WORKER` set) skip the check: they make no registry request and
print no version line. When no build is installed yet,
`hook` also skips the first-run download: it records nothing, prints one line saying so and exits
0, and the next command you run installs the build.

Available updates still install in the background at most daily, with package-integrity
verification, and take effect on a later command. `CI`, `CODEMUSTER_NO_UPDATE`, and exact
version pins disable automatic checks and downloads. Explicit `update` remains available when
not pinned; `update --check` does not install. Updating does not replace a running binary.

[`CHANGELOG.md`](../CHANGELOG.md) records changes by version and ships in every npm package.
After a successful `codemuster update`, the launcher displays each release newer than the old
version through the installed version, excluding Unreleased and later entries. Older packages
without notes still install successfully and say that release notes were not included.
Run `npm install -g codemuster` to get updated launcher JavaScript; binary self-updates alone
cannot add these new notices to an older launcher.

Before `run`, `verify`, `fix`, or `intelligent-config` calls any agents, a preview prints the maximum worker count,
provider, model and thinking/effort setting to stderr. `-j 50` means up to 50 concurrent calls;
fewer pending units may use fewer workers. The preview reflects the values requested with
`--model` and `--effort`. Omitted values say `provider default`, because the external agent owns
those defaults; Gemini's unsupported effort setting says `not supported`.

Interactive terminals show a ten-second countdown. Enter starts immediately; Escape or Ctrl+C
cancels so you can rerun with different settings. Verification refresh, force requeue and fix
work start after the countdown. CI or redirected input/output skips the wait while retaining
the settings preview. Commands that do not launch agents have no countdown.

In a terminal, the preview uses a compact `</> CODEMUSTER` banner and separate,
emphasized rows for provider, model, thinking and worker count. Audit progress
includes completed/total counts and a bar: green for recorded work, yellow for
skipped work, red for rejected/invalid responses. The text still identifies the
outcome when colors are unavailable. General progress lines highlight elapsed time.
`intelligent-config` shows an animated elapsed-time line while it works, followed
by an explicit success, failure or cancellation result. It does not guess a
percentage or claim to see the agent's internal reasoning.

Set `NO_COLOR=1` to disable colors while retaining the readable layout and countdown.
`TERM=dumb`, CI and redirected output use plain text with no animation; dumb terminals
also skip the countdown. Reports and machine-readable stdout formats are unchanged.


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

`scan`, `next`, `run`, `verify`, `fix`, `done`, and `validate` hold one exclusive coordinator lock in
the common Git directory. A competing command, including in a linked worktree, fails clearly.
The lock is released when the process exits; a leftover lock file need not be deleted. Reading
packs is not a work lease, so manual reviewers still need one owner per assigned unit.

Every process CodeMuster starts (git, the audit tools, the agents, `test_command`, node) gets an
empty standard input and ends with CodeMuster (D77). On Windows the operating system kills them
however CodeMuster exits, including a forced kill. On macOS and Linux CodeMuster kills them when it
exits or receives SIGTERM or SIGHUP; SIGKILL cannot be caught, so after `kill -9` its children, and
the native binary under a SIGKILLed npm launcher, keep running until you stop them. Build servers
started by `test_command`, such as MSBuild worker nodes, end with CodeMuster as well. Roslyn's build
host, started by C# mapping itself, exits when its pipe to CodeMuster breaks.

Validation edits outside the selected tracked-file scope stop integration and are preserved
for inspection. A failed commit never records a successful fix. If a commit succeeds but the
ledger write fails, keep the commit, inspect the diagnostic, and run forced verification to
reconcile the finding. Avoid concurrent edits to the checkout during integration.

Fix packs are rendered only when a worker slot is available. Whole-file packs exceeding
`slice_token_budget * 4` characters are too large. During `run`, `verify`, and interactive
`next`, these units are marked `skipped` once with a path-specific reason, without an agent
call or retry. Other units continue; size skips alone do not make the run exit nonzero.
`next` moves forward to the next eligible pack and reports skips on stderr.

Skipped units remain visible in `status` and `report`, do not count as analyzed, and stay out
of subsequent queues. After increasing the budget or splitting the file, run `scan` to requeue
them. `run --force` also requeues skipped units within its scope, alongside re-auditing done
units. Other failures still follow `--attempts` and cause a nonzero exit when exhausted.
Repair pack failures remain failures and cannot claim a successful fix.

The ledger uses schema version 7 to identify support for persisted skipped states. Upgrading
preserves existing rows; older CLI versions ask for an update rather than opening this ledger.
No source is silently truncated or counted as reviewed. This bounds individual prompt
construction, not total repository memory. No representative maximum-scale benchmark has
been completed.

Schema version 8 adds the stored code map (D60). Every `scan` that runs the mappers (every
scan except `scan --mode file`, which leaves the last map as it was) replaces it in one
transaction: each symbol's id, path, line range, kind, signature, body hash and container
(its C# type, TypeScript class, or file), every call edge, every entry point, the commit the
scan was at, and the mapper diagnostics. When a language's mapper fails, the map keeps what
the other mappers returned and records the failed language and its diagnostic, so it reads
as partial. Source text is never stored. The map is not a unit: it does not change coverage,
`status`, `next`, `run` or fingerprints. Opening an older ledger upgrades it to schema 8 with
its rows kept; older CLI versions then ask for an update rather than opening it. `map` reads it
(see [Code map](#code-map)). An edge kind a newer build stored and this one does not know is
skipped when the map is read, with a diagnostic, so the map reads as partial instead of failing.
Schema 8 also adds the `agent_calls` table: one row per agent call with its run, unit, requested
and answering model, token counts, the harness-reported cost, and the cost and its source fixed
when the call was recorded (D63, see [Spend](#spend)). A ledger that a development build had
already moved to schema 8 gains the table the next time it is opened.

The stored map also links the UI to the API (D61). The TypeScript mapper reads each `fetch`,
`axios` verb helper, `axios(config)` / `axios.request(config)` and `axios.create({ baseURL })`
instance call whose URL it can fold from literals, templates, `+` and constants: the method
comes from the call (GET when none is given, ANY when it is chosen at runtime), each `${...}`
becomes a parameter segment, and the origin, query string and fragment are dropped. Scan matches
each call to an `http` entry point (a C# action, or a JavaScript or TypeScript route) by method and route, segment by segment, and stores an edge
of kind `http` from the calling function to the action or route handler. Calls that match no endpoint, URLs built
at runtime, and (when any call was found) endpoints no call reaches are stored as map diagnostics
starting with `http: `; they are not findings and do not make the map partial. Scan prints one
progress line on stderr such as `linked 1 UI call to an endpoint; 1 call and 1 endpoint
unmatched`; its stdout, units, slices and fingerprints do not change.

The TypeScript mapper also gives each TypeScript and JavaScript symbol a normalized hash (D68):
a hash of its body's tokens with every identifier and literal replaced, so bodies that differ
only in names, strings, numbers, whitespace or comments share it. It is stored with the symbol.
The mapper also reads the UI's structure (D69): each page route; navigation entries (a link with
a static `href` or `to` inside a `nav`, `aside`, `header`, `menu` or a Nav, Menu or Sidebar
component, any `NavLink`, and arrays of `{ label, href }` objects); section headings (`h1` to
`h3`, `legend`, and titled `Section`, `Card`, `Panel` or `Group` components); and form controls
(`input`, `select`, `textarea`, and components such as `Switch`, `Toggle` or `Checkbox`) with
their label, the heading they sit under, the page route and `path:line`, at most 200 per file.
Scan keeps that structure in memory only; the ledger does not store it.

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
