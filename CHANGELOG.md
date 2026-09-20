# Changelog

User-visible changes by released version. Add upcoming changes under Unreleased;
move them to a dated version heading before publishing. Historical entries below
start with 0.2.8.

## Unreleased

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
