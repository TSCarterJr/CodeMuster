# Changelog

User-visible changes by released version. Add upcoming changes under Unreleased;
move them to a dated version heading before publishing. Historical entries below
start with 0.2.8.

## 0.3.6 - 2026-09-25

Fixes chosen from a review of 0.3.5 (`docs/product-value-review.md`).

- A `git`, `node` or `dotnet` program committed to a repository, or sitting in the
  folder you run CodeMuster from, no longer runs in place of the real tool. Git,
  Node.js, agents, audit tools and `test_command` are found only in absolute `PATH`
  directories, and on Linux and macOS a file without the execute bit is skipped. A
  relative `PATH` entry such as `node_modules/.bin` no longer satisfies
  `test_command` (use `["npx", "jest"]`). On Windows, programs CodeMuster starts
  inherit `NoDefaultCurrentDirectoryInExePath=1`, so a script run by `test_command`
  must name a program in the current folder with a path, such as `.\build.cmd`.
- An untracked nested repository or worktree, such as a Claude Code worktree under
  `.claude/worktrees/`, no longer makes `status`, `report`, `scan`, `fix`, `verify`
  or the hook exit 1, and many untracked files no longer slow them down. The change
  warning names what changed (`web/lib/index.ts changed since the last scan`) and
  no longer fires for untracked or excluded files, including `report --out
  audit.md`, or for staging unchanged content. `codemuster hook` always exits 0, so
  it never fails an agent's tool call. `init` adds PowerShell to the Claude hook
  matcher and repairs an existing CodeMuster matcher.
- Committing content that was analyzed while modified no longer marks it stale
  under `core.autocrlf=true` (the Git for Windows default) or in a SHA-256
  repository.
- `verify` and `scan` no longer re-check unchanged findings on every cycle. Each
  re-check was a model call.
- A failed dependency audit (offline, registry or restore error) warns and keeps the
  earlier vulnerable-package findings instead of recording none. A folder with
  lockfiles for more than one package manager is audited once, with a warning that
  names the tool used, instead of making every scan exit 2. When the preferred tool
  is not installed, the next lockfile's tool is used. pnpm findings no longer call
  every vulnerable package indirect.
- `fix` works with `diff.noprefix`, `color.ui=always`, `diff.external` and similar
  Git settings, which used to discard every repair. It never records a finding
  fixed unless the file changed. It runs `test_command` once on the unmodified tree
  before calling any agent and stops when the suite already fails or the program is
  missing; `fix --allow-failing-tests` skips that check.
- With TypeScript 7, which has no JavaScript compiler API, the mapper uses
  `@typescript/typescript6` from the same project when it is installed, and
  otherwise says in one line what to install instead of printing a Node stack trace.
- Argument mistakes name the problem and show the command's usage line instead of
  the full overview, and a mistyped command gets a suggestion. `--name=value` and
  `-j4` work. `next`, `status`, `done` and `skill` reject options they do not read,
  so `next --pth web` no longer reviews the whole repository. Errors start with
  `error:` and no longer end in `(Parameter 'name')`.
- Scans with `exclude`, lens or `user_experience` globs no longer slow down with the
  number of files: 3,000 files and 10 globs went from about 10 s to 1.5 s. The npm
  launcher skips its update check for `codemuster hook` and inside CodeMuster
  workers.

Use `npm install -g codemuster@0.3.6` once to receive the launcher change as well as
the native binary; older launchers update only the binary.

## 0.3.5 - 2026-09-21

- A repair that cannot be integrated no longer ends the whole run. Previously one
  such file cancelled every other worker mid-call, so a parallel `fix` could pay
  for many agent calls and keep no repairs. The run now stops starting new
  repairs, lets the calls already running finish, and retains each of those
  repairs unapplied in its own worker checkout with the path reported, so they
  can still be recovered. The file that could not be integrated is reported and
  the command exits non-zero.
- The message for a file changed outside the allowed scope during validation no
  longer blames the configured test command. CodeMuster compares the checkout
  before and after validation, so it now says the change came either from the
  test command or from something else using the same checkout. Running `fix`
  while another tool or agent writes to the same checkout is what produces this.

## 0.3.4 - 2026-09-20

- `codemuster fix` no longer stops the whole run when one file is too large for a
  whole-file pack. Previously a single oversized file at the head of the queue
  ended the run with nothing repaired. That file is now recorded as skipped with
  its reason, every other file is still repaired and committed, the summary
  reports the skipped count, and the exit code stays zero. Raise
  `slice_token_budget` in `.codemuster/config.json` or split the file and the next
  `fix` picks it up, with no extra flag and no rescan.
- A file whose repair already completed is revisited when the audit later confirms
  a finding in it that the repair never answered.

## 0.3.3 - 2026-09-19

- Show the actual Codex model and thinking level before agent work by reading
  effective repository settings. Explicit flags override those settings, and all
  workers use the values shown. If lookup fails, request explicit model and effort
  instead of showing unknown defaults.
- Forward launcher-only cancellation on Unix while preserving graceful Windows
  Ctrl+C handling. Improve subprocess cleanup and retain isolated repair worktrees
  when a worker fails.
- Correct C# and TypeScript mapping for inherited actions, linked documents,
  standalone projects, operators and method-ID collisions; surface TypeScript
  configuration diagnostics.
- Strengthen configuration validation, glob matching, audit-pack code fences,
  stale dependency handling and per-target UX evidence requirements.
- Improve report layout, protect build output during package staging, and fix
  test-process and temporary-file cleanup.

Use `npm install -g codemuster@0.3.3` to receive the launcher changes as well as
the native binary.

Version 0.3.2 was tagged but not published; its release was stopped for the
Windows cancellation correction.

## 0.3.1 - 2026-09-19

- Add a compact terminal banner, highlighted provider/model/thinking settings and
  countdown bar before agent commands. Add colored audit progress counts and an
  elapsed activity indicator for intelligent configuration, with explicit results.
- Respect `NO_COLOR`, keep redirected/CI output plain, and disable animation and
  the countdown for `TERM=dumb` terminals.

## 0.3.0 - 2026-09-19

- Show the intervening release notes after `codemuster update`.
- Check for a newer version on every normal command and report the result without
  changing command output or stopping work when the registry is unavailable.
- Include this changelog in the launcher and every platform package.
- Preview the worker limit, provider, model and thinking level before `run`, `verify`
  or `fix`. Interactive terminals wait ten seconds; Enter starts immediately and
  Escape or Ctrl+C cancels. CI and redirected commands start without waiting.
- Add `intelligent-config` to inspect repository context with an AI agent and apply
  validated exclusions, scoped lenses and detected test setup. Preserve existing
  custom settings and keep an exact backup before replacing the config.

Upgrade from 0.2.9 or earlier with `npm install -g codemuster@0.3.0` to
install the new launcher. Older launchers update only the native binary, so their
`codemuster update` cannot add release-note or per-command update notices.

## 0.2.9 - 2026-09-19

- Exclude non-code documents, data, configuration and database files from AI review,
  including Markdown, text, JSON, YAML, XML, CSV, Office documents and SQLite files.
  Mapper metadata and ecosystem dependency audits retain their required inputs.
- Mark oversized packs as skipped once and continue other work. Skips retain a
  reason, consume no agent attempts or retries, and never count as reviewed.
- Show skipped work in status and reports. Rescan or use `run --force` to retry it.
- Upgrade the ledger to schema 7 while preserving audit history. Older CLIs cannot
  open this schema. Run `codemuster scan` after upgrading to refresh exclusions.

## 0.2.8 - 2026-09-15

- Add settings-driven proactive plugin workflows for Claude and Codex, with shared
  setup, skills and hooks.
- Isolate fix workers, enforce repair scope, preserve dirty work through explicit
  stash handling, and validate fixes before recording success.
- Add opt-in static usage assessments and browser-backed user-experience reviews.
- Prevent dead-code scans from failing on wall-clock regex timeouts.
- Support exact CLI version pins and separate update caches by OS and architecture.
