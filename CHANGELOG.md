# Changelog

User-visible changes by released version. Add upcoming changes under Unreleased;
move them to a dated version heading before publishing. Historical entries below
start with 0.2.8.

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
