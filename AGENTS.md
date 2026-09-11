# Agent instructions for CodeMuster

Read this before touching anything. Applies to every agent (Claude Code, Codex, Gemini CLI,
OpenCode) and to humans.

## What this is

A CLI plus one skill that audits an entire codebase with provable coverage. The CLI owns the
state (SQLite ledger) and the loop; the AI model is called once per unit of work. Read
`idea.md` for the full concept and `DECISIONS.md` for every settled choice. Do not reopen a
decision in code; add a new entry to `DECISIONS.md` and get it agreed first.

## How to pick up work

1. Open `MVP-Checklist.md`. Take the first task marked `[ ]` in order, unless the task says it
   can run in parallel. Do not skip ahead without noting why in the task's Notes.
2. Mark it `[~]` with today's date and your name (`claude`, `codex`, `tim`, ...).
3. Write the failing test(s) listed under the task first. Run them. They must fail.
4. Write the minimum code that makes them pass. Run the full suite: `dotnet test`.
5. Refactor only what the task touched. Run the suite again.
6. Mark the task `[x]` with the date. Put anything surprising in Notes.
7. One task per commit. Commit message starts with the task ID, e.g. `T2.3: stat cache skips unchanged files`.

If a task turns out to be wrong or unnecessary, mark it `[-]` with the reason. Do not delete
tasks.

## Test-driven, always

- No production code without a failing test that demands it. The composition root in `Cli`
  (wiring only) is the single exemption.
- Application tests use in-memory fakes of the Domain interfaces. Infrastructure tests use real
  SQLite in a temp directory and real `git` in a temp repo. Mapper tests use the fixture repos
  under `fixtures/` and assert exact symbol and edge sets (golden files).
- End-to-end CLI tests run the real binary against a fixture repo with `--agent fake`, so the
  whole loop is tested with zero LLM calls.
- Tests must pass on Windows, macOS, and Linux. Never assert on path separators; normalize to
  forward slashes at the boundary and compare normalized strings.

## Code style

- Keep it simple. Prefer the standard library. Prefer one obvious function over a pattern.
  Delete before adding. No abstractions that only one implementation uses, except the Domain
  interfaces that exist so Application can be tested with fakes.
- No code comments unless the logic is genuinely hard to follow, and then say why, not what.
  XML `<summary>` on public types and members in `Domain` and `Application` only.
- `Nullable` enabled, `TreatWarningsAsErrors` on, file-scoped namespaces, `record` for models,
  no static mutable state, `CancellationToken` on every async boundary.
- All I/O (file system, git, SQLite, process spawning, clock) sits behind an interface in
  `Domain` and is implemented in `Infrastructure`. Application never touches I/O directly.
- Paths stored or compared anywhere are repo-relative with forward slashes. Timestamps are UTC
  ISO 8601 strings. Use invariant culture for all formatting and parsing.
- Spawn processes with an argument list, never a shell string. On Windows resolve npm `.cmd`
  shims explicitly.
- Do not add a NuGet package without a task that names it. Current allowed set:
  `Microsoft.Data.Sqlite`, `Microsoft.CodeAnalysis.CSharp.Workspaces`,
  `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.Build.Locator`, xunit and the test SDK.

## Layout

```
src/CodeMuster.Domain/              models, interfaces, findings schema. Zero dependencies.
src/CodeMuster.Application/         use cases: Scan, Next, Done, Status, Estimate, Report, Run, Doctor, Verify.
src/CodeMuster.Infrastructure/      SQLite ledger, git, file system, clock, agent adapters, fallback mapper.
src/CodeMuster.Mapping.CSharp/      Roslyn batch mapper.
src/CodeMuster.Mapping.TypeScript/  embedded TS compiler-API script + node runner.
src/CodeMuster.Cli/                 composition root, verb dispatch, console output.
tests/<project>.Tests/               one test project per source project.
fixtures/                            small repos with known call graphs and planted defects.
skill/SKILL.md                       the one skill file, installed by the CLI.
npm/                                 the `codemuster` npm launcher (D33) and its `node --test` tests.
.github/workflows/                   test matrix (3 OSes) and release matrix.
```

Dependency direction: `Cli -> Application -> Domain`; `Infrastructure`, `Mapping.* -> Domain`.
Nothing depends on `Cli`. `Application` never references `Infrastructure` or `Mapping.*`.

## Things not to do

- Do not write an LSP client or depend on a language server (D08).
- Do not use modified time to decide staleness (D05).
- Do not branch on unit kind in `next`, `done`, `status`, or `run` (D06), except for the verify pack and its verdict (D27).
- Do not change the findings schema without a new decision (D11).
- Do not auto-install anything. `doctor` prints commands. The one exception is the npm launcher keeping CodeMuster itself up to date (D33).
- Do not commit `.codemuster/ledger.db` in any repo, including fixtures.
