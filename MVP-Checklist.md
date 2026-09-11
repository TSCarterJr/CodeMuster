# MVP Checklist

Single source of truth for implementation status. Every agent and every human works from this
file. Rules for picking up a task are in `AGENTS.md`. Settled choices are in `DECISIONS.md`.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[-]` cut or deferred

Every task lists **Tests first**: the failing tests to write before any production code.
Tasks within a phase run in order unless marked *(parallel)*. Phases run in order.

---

## Phase 0: Bootstrap and the one real unknown

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T0.1 | `git init`, MIT license, `.gitignore` (bin, obj, `.codemuster/ledger.db`, `node_modules`), `.editorconfig`, `.gitattributes` (LF everywhere), `Directory.Build.props` with `Nullable`, `TreatWarningsAsErrors`, `LangVersion latest`, `InvariantGlobalization`. | `[x]` | claude 2026-09-10 | Remote: https://github.com/TSCarterJr/CodeMuster (private until release). |
| T0.2 | Solution with the six source projects and six test projects from the AGENTS.md layout, all `net10.0`, xunit. Empty `Program.cs` that returns exit code 2 with usage text. | `[x]` | claude 2026-09-10 | xunit 2.9.3 + test SDK 17.14.1 via `tests/Directory.Build.props`; assembly name is `codemuster`; CLI e2e tests run `dotnet codemuster.dll` from the test output dir. |
| T0.3 | **Spike: SQLite + Roslyn in a self-contained build on all three OSes.** Console app opens a SQLite DB, writes one row, loads `fixtures/mixed-repo` solution with MSBuildWorkspace, resolves one invocation to its callee. Published `-r win-x64`, `osx-arm64`, `linux-x64` self-contained single-file. Record binary size and startup time in Notes. | `[ ]` | | This de-risks D02/D03/D08. If MSBuildWorkspace fails in single-file, fall back to framework-dependent global tool as the only route and record a new decision. |
| T0.4 | CI: `.github/workflows/test.yml` runs `dotnet test` on `ubuntu-latest`, `windows-latest`, `macos-latest`. | `[x]` | claude 2026-09-10 | Node must be on the runner for T8. No unit test possible for YAML; verified by the first push. setup-dotnet 10.0.x, setup-node 22, restore/build/test split so failures are attributable. |
| T0.5 | `fixtures/mixed-repo`: tiny C# minimal API (2 endpoints, one service behind an interface registered in DI, one repository, one shared helper, one hosted service, one dead method) plus a TS Next.js-shaped folder (2 `page.tsx`, one component, one hook, one barrel re-export, one API client). Planted defects: one missing tenant filter in a query, one unescaped HTML render. A `Migrations/` folder and a `*.g.cs` file that must be excluded. Must build with `dotnet build` and `npm ci` (typescript only). | `[ ]` | | Golden expected call graph lives beside it as `expected-map.json`, written by hand in T7/T8. |

## Phase 1: Domain

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T1.1 | Models: `FileRecord` (path, language, content_hash, size, mtime_at_analysis, first_seen, last_seen, last_commit, last_commit_at, excluded_reason, deleted_at, summary, summary_hash), `Unit` (id, kind, key, fingerprint, status, fidelity, summary, summary_hash), `UnitMember` (unit_id, path, symbol, member_hash, distance), `Run`, `Analysis`, `Finding`. **Tests first:** record equality, `Unit.Fingerprint` is deterministic over member order. | `[x]` | claude 2026-09-10 | Kind is a closed enum: `File, Slice, Orphan, Verify`. Status adds `Retired` for units whose key left the tree (never handed out, never deleted). `Unit.LensHash` records the lens hash of the analysis that made it Done so scan can mark lens changes stale. `Finding` lives in T1.2. Language is a string constant, not an enum. |
| T1.2 | Findings schema (D11) as a Domain type plus JSON serialization contract. **Tests first:** round-trip; rejects missing required fields; rejects unknown severity; accepts the exact sample response the skill will ask the model to produce. | `[x]` | claude 2026-09-10 | The sample JSON in the test is the same text that goes into SKILL.md: `AnalysisResponseJson.Sample`, pinned byte-identical through a serialize round-trip. `DomainJson.Options` sets LF newlines and relaxed escaping so that holds on Windows too. Confidence is a 0..1 number. |
| T1.3 | Interfaces: `ILedger`, `ISourceTree` (list files with hashes and stat info, read file, HEAD commit, last commit per path), `ICodeMapper` (repo path -> `CodeMap`), `IAgentAdapter` (unit pack -> raw text), `IClock`, `IFileSystem`. **Tests first:** none (interfaces), but every later Application test uses fakes of these. | `[ ]` | | Keep these small. Add a method only when a use case needs it. |
| T1.4 | `CodeMap` model: `Symbol` (id, path, range, kind, signature, body_hash), `Edge` (from, to, kind: Call/Implements/Overrides/Imports), `EntryPoint` (symbol_id, kind, display e.g. `GET /v1/quotes`), `ResolutionStats` (resolved, unresolved, top unresolved names). **Tests first:** JSON round-trip against `fixtures/mixed-repo/expected-map.json`. | `[x]` | claude 2026-09-10 | Golden file arrives in T7.1/T8.1; until then the round-trip runs on an inline map. `Symbol.Kind` and `EntryPoint.Kind` are free strings chosen by the mapper; `EdgeKind` is the closed enum. |
| T1.5 | Language and exclusion rules: extension -> language map; generated-file filter (`*/Migrations/*`, `*.g.cs`, `*.Designer.cs`, `*.min.js`, lockfiles, `*.d.ts`, snapshots, `linguist-generated`). **Tests first:** table-driven cases for every rule, including a Windows-style input path normalized first. | `[x]` | claude 2026-09-10 | Also `RepoPath.Normalize` and a gitignore-style `Glob.IsMatch` (used by lens globs in T3.6). `Migrations` and `__snapshots__` match directory segments only, case-sensitive; suffix and binary-extension rules are case-insensitive. Adds a `binary` reason for image/archive/font extensions so packs never contain bytes. |

## Phase 2: Infrastructure, ledger and git

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T2.1 | SQLite `ILedger`: schema creation and migration table, `files`, `units`, `unit_members`, `runs`, `analyses`, `findings`. Busy timeout set. **Tests first:** creates schema in a temp dir; opening twice is idempotent; `PRAGMA user_version` gates migrations. | `[ ]` | | Raw SQL, no ORM. |
| T2.2 | `ILedger` file upsert and stat cache (D05). **Tests first:** unchanged mtime+size skips hashing (fake hasher call count is 0); changed mtime with same hash keeps analysis and updates mtime; changed hash invalidates every unit that has the file as a member; a record written in the same second as the file's mtime is flagged for rehash. | `[ ]` | | |
| T2.3 | `ILedger` units: insert, mark stale by member hash, `next(batch)` returns oldest not-done or stale first, `done` records analysis and findings atomically. **Tests first:** `next` never returns a done unit whose fingerprint is current; `done` on a unit whose fingerprint changed since `next` is rejected with a clear error; two writers with busy timeout don't corrupt. | `[ ]` | | |
| T2.4 | `ILedger` deleted-file handling: files absent from the tree get `deleted_at`, their units go stale, rows are never deleted. **Tests first:** as stated. | `[ ]` | | |
| T2.5 | Git `ISourceTree`: `git ls-files -s` parse (mode, blob SHA, path), `git status --porcelain` for dirty files, `git rev-parse HEAD`, single `git log --name-only --format=%H%x00%cI` walk to fill last commit per path. **Tests first:** against a temp repo built in the test with three commits; a dirty file gets our own hash, not the index SHA; paths come back with forward slashes on every OS. | `[ ]` | | One git process per command, never per file. |
| T2.6 | Content hasher for dirty files that matches git's blob SHA for the same bytes. **Tests first:** hash of a known string equals `git hash-object` output. | `[ ]` | | So the ledger never holds two hash schemes. |

## Phase 3: File units, the coverage floor

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T3.1 | `Scan` use case, file mode: discover, apply exclusions, upsert files via stat cache, create or refresh one `File` unit per included file, record `Run`. **Tests first (fakes):** first scan creates N units; second scan with nothing changed creates 0 and hashes 0; one changed file produces exactly one stale unit; excluded files appear in `files` with `excluded_reason` and no unit. | `[ ]` | | |
| T3.2 | `Status` use case: analyzed / total at HEAD, stale count, excluded count, resolution rate when present, low-fidelity count. **Tests first:** numbers match a seeded ledger; output is plain text and stable for the CLI test. | `[ ]` | | |
| T3.3 | `Next` use case: returns unit pack(s) for a `File` unit: path, language, content, lens instructions, findings schema sample, expected response format. **Tests first:** pack for a file unit contains the file content verbatim; `--batch 3` returns 3 distinct units; empty when complete. | `[ ]` | | Pack is one markdown document written to stdout or `--out <path>`. |
| T3.4 | `Done` use case: parse response JSON (T1.2), validate every finding path exists in the unit's members, store analysis, findings, summary, fingerprint. **Tests first:** valid response marks done; a finding for a path outside the unit is rejected; invalid JSON records a failed analysis and leaves the unit not-done. | `[ ]` | | |
| T3.5 | `Estimate` use case: bytes / 4 per pending unit, total and per-kind. **Tests first:** matches seeded sizes. | `[ ]` | | |
| T3.6 | Lens config: `.codemuster/config.json` with named lenses (instructions, file globs, language filter), lens hash stored on each analysis; changing a lens marks its units stale. **Tests first:** hash changes when text changes; stale propagation. | `[ ]` | | Ship one default lens: general correctness and security. |
| T3.7 | CLI verbs `scan`, `status`, `next`, `done`, `estimate` with hand-rolled parsing (D18). **Tests first (end-to-end, real binary, fixture repo):** `scan` then `status` shows `0/N`; `next` writes a pack; `done --findings f.json` then `status` shows `1/N`; unknown verb exits 2 with usage. | `[ ]` | | |
| T3.8 | `Init` use case and setup gate (D24). `init` writes `.codemuster/config.json` (default lens from T3.6) and handles `.gitignore` per D24. Every other use case except `doctor` fails with `NotInitialized` when config is missing; the CLI maps it to exit 2 and the `run codemuster init` hint. **Tests first (fakes):** init in an empty repo creates config and appends the ledger line; init is idempotent; existing `.gitignore` already covering the ledger is left byte-identical; `--no-gitignore` leaves `.gitignore` alone; `scan`/`status`/`next`/`done` on an uninitialised repo return `NotInitialized`. **E2E:** binary `scan` in a fresh fixture copy exits 2 with the hint; after `init --yes` it exits 0. | `[ ]` | | The T3.7 e2e tests gain an `init --yes` step when this lands. Lives in Application so the gate is one guard, not a check per verb. |

## Phase 4: Skill and interactive mode

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T4.1 | `skill/SKILL.md` in Agent Skills format: run `scan`, loop `next` / analyze / `done` until `status` is complete, the exact response JSON sample from T1.2, and the rule "the pack is the scope; you may read more, but every finding must cite a path in the pack." **Tests first:** the JSON sample in SKILL.md is byte-identical to the T1.2 test fixture (test reads the file). | `[ ]` | | Keep it under one screen. Logic lives in the CLI. |
| T4.2 | `skill install --for claude\|codex\|gemini\|opencode [--global]` writes SKILL.md to the right folder. **Tests first:** each target writes to the expected relative path in a temp home / temp repo; running twice is idempotent. | `[ ]` | | Verify each harness's current skill folder convention against its docs at implementation time; record paths in Notes. |
| T4.3 | Manual check: run the interactive loop in Claude Code and Codex on `fixtures/mixed-repo`. Record in Notes what each harness needed. | `[ ]` | | Not automatable. Tim or an agent in a real session. |

## Phase 5: Headless driver

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T5.1 | `IAgentAdapter` implementations: `fake` (returns canned JSON from a file, used by all e2e tests), `claude`, `codex`, `gemini`, `opencode`. Each knows its headless invocation and how to get the model's final text. **Tests first:** fake adapter round-trip; each real adapter builds the expected argument list (no process launched); on Windows the resolved executable ends in `.cmd` when only the shim exists. | `[ ]` | | Check current headless flags for each CLI at implementation time and record them in Notes. |
| T5.2 | `Run` use case: pull `next --batch j`, spawn `j` adapters, feed results to `Done`, loop until empty. Ctrl-C cancels in-flight units cleanly and leaves them not-done. Retry a failed unit up to N times then mark failed. **Tests first (fakes):** j=3 runs three concurrently; cancellation mid-run leaves the ledger consistent and a second `run` resumes at the right count; a unit that fails N times is reported and skipped, not silently dropped. | `[ ]` | | |
| T5.3 | CLI `run --agent X -j N [--lens L] [--force]`. Prints progress `done/total` per unit. **Tests first (e2e):** `run --agent fake` on the fixture completes all units; `--force` re-analyzes done units. | `[ ]` | | |

## Phase 6: Report

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T6.1 | `Report` use case, markdown: coverage header (analyzed/total at commit, stale, low fidelity), findings grouped by severity then path, unit summaries as an inventory table. **Tests first:** golden markdown for a seeded ledger. | `[ ]` | | |
| T6.2 | CLI `report [--out file]`. **Tests first (e2e):** after `run --agent fake`, report contains the planted findings. | `[ ]` | | SARIF is a later task (L2). |

**Milestone A: file-mode v0.** Everything above works end to end on `fixtures/mixed-repo` on three OSes with `--agent fake`, and once for real with `--agent claude`.

## Phase 7: C# mapper (Roslyn)

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T7.1 | Hand-write `fixtures/mixed-repo/expected-map.json` for the C# half: every method symbol, every call edge, implements edges, the two HTTP entry points and the hosted service entry point. | `[ ]` | | This is the golden file. Get it right by reading the fixture, not by running the mapper. |
| T7.2 | `RoslynMapper : ICodeMapper`: locate SDK (`MSBuildLocator`), open `.sln` (or all `.csproj` if none), walk every method body, resolve invocations, object creations, and property accesses to symbols, emit `Symbol` with signature and body hash. **Tests first:** symbol set equals golden. | `[ ]` | | Body hash is over the normalized syntax text of the member, so whitespace-only edits don't invalidate. |
| T7.3 | Interface dispatch: for a callee that is an interface or abstract member, add edges to every implementation found by `SymbolFinder`. When DI registration is detectable (`AddScoped<I, T>()` etc.), prefer that binding and mark the edge `Bound`. **Tests first:** the fixture's `IQuoteService` resolves to `QuoteService` only, via DI binding. | `[ ]` | | Biggest source of "the tool missed the real code." |
| T7.4 | Entry-point detection: Minimal API `Map*` calls, controller actions with HTTP attributes, `IHostedService`/`BackgroundService`, plus configurable regex patterns from config. Display string like `GET /v1/quotes`. **Tests first:** the three fixture entry points, with display strings. | `[ ]` | | |
| T7.5 | `ResolutionStats`: count resolved vs unresolved call sites, top 20 unresolved names. **Tests first:** fixture resolves 100%; deleting `obj/` (unrestored) drops well below threshold and the mapper reports why. | `[ ]` | | This is the health gate (D09). |
| T7.6 | Cross-check sampler: for a sample of symbols, plain text search for the name; edges the mapper lacks but text finds are recorded as `possibly_incomplete` on the unit. **Tests first:** a deliberately hidden reflection call in the fixture trips it. | `[ ]` | | |

## Phase 8: TypeScript mapper (compiler API) *(parallel with Phase 7)*

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T8.1 | Hand-write the TS half of `expected-map.json`: functions, components, hooks, the barrel re-export resolved to its origin, the two page entry points, the API client leaf. | `[ ]` | | |
| T8.2 | Embedded `map.js`: load `typescript` from the target repo's `node_modules`, create a program from `tsconfig.json`, walk call expressions and JSX element usages, resolve via the checker, print the code map JSON. **Tests first:** run under `node` against the fixture, output equals golden. | `[ ]` | | Script is a resource inside `Mapping.TypeScript`, extracted to a temp file at run time. |
| T8.3 | `TypeScriptMapper : ICodeMapper`: locate `node`, extract script, spawn, parse. Entry points: `app/**/page.tsx`, `pages/**`, plus config globs. **Tests first:** same golden through the C# side; missing `node_modules` yields a clear diagnostic and low-fidelity fallback instead of an empty map. | `[ ]` | | |

## Phase 9: Slices

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T9.1 | `CompositeMapper`: runs every applicable mapper for the repo's languages, merges code maps, falls back to `ImportGraphMapper` (regex imports, name-match calls, fidelity `Low`) for languages with no mapper or when a mapper fails. **Tests first:** mixed fixture yields one merged map; forced C# failure yields Low fidelity for `.cs` only. | `[ ]` | | |
| T9.2 | `SliceBuilder`: from each entry point, BFS over Call/Bound/Implements edges to collect method-level members with distance. **Tests first:** the `GET /v1/quotes` slice equals the hand-listed member set from the golden; the dead method is in no slice. | `[ ]` | | |
| T9.3 | Token budget (D07): full body for members up to distance `d`, then signature + summary, then signature only, until the pack fits the configured budget. Pack lists every outlined member. **Tests first:** with a tiny budget the entry point is still full-body and the leaf helper is signature-only; the outlined list is complete. | `[ ]` | | Default budget goes in config; tune during dogfood. |
| T9.4 | Slice fingerprint = hash over member `(path, symbol, body_hash)`; a change to one member marks only slices containing it stale. **Tests first:** editing the shared helper marks both endpoint slices stale; editing the dead method marks none. | `[ ]` | | |
| T9.5 | Orphan pass: every included file with no member in any slice becomes an `Orphan` unit (file mode pack, flagged as unreachable). **Tests first:** the dead method's file becomes an orphan; coverage total = slices + orphans and every included file is a member of at least one unit. | `[ ]` | | |
| T9.6 | `Scan` in slice mode is the default when any mapper is available; `--mode file` forces file units. Health gate: below resolution threshold, slices are created but `status` refuses `complete` and prints the top unresolved names. **Tests first:** as stated, with the unrestored fixture. | `[ ]` | | |
| T9.7 | `Next` pack for a `Slice` unit: entry point display, members in call order with degradation, lens, schema. `Done` accepts findings on any member path. **Tests first:** pack for `GET /v1/quotes` contains the handler and the service method bodies in order. | `[ ]` | | |
| T9.8 | `Status` and `Estimate` show `slices done/total`, `orphans done/total`, blast radius before a run ("N units stale, touching M endpoints"). **Tests first:** numbers match. | `[ ]` | | |

**Milestone B: slice-mode v1.** Fixture audited end to end as slices; `status` reads `2/2 endpoints, 1/1 background, 1/1 orphans`.

## Phase 10: Verify pass

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T10.1 | `Verify` units: one per unverified finding, pack = finding + its unit's pack + instruction to refute. Response: `confirmed \| refuted \| unsure` with reason. `done` updates `verify_status`. **Tests first (fakes):** confirmed and refuted both persist; a refuted finding is excluded from the default report. | `[ ]` | | Same `next`/`done`/`run` machinery (D06). |
| T10.2 | CLI `verify` (alias for `run --kind verify`) and `report --include-refuted`. **Tests first (e2e):** with a fake that refutes one planted finding, the report drops it. | `[ ]` | | |

## Phase 11: Doctor

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T11.1 | `Doctor` use case: for each language present, a functional probe (D09): .NET SDK found and solution loads and one symbol resolves; `node` found and `typescript` resolves and program builds; git present. Reports `working / loaded-but-empty / failed`, time to ready, and the fix command for each failure. **Tests first (fakes):** each of the three states renders the expected line; never spawns an install. | `[ ]` | | |
| T11.2 | CLI `doctor`. **Tests first (e2e):** exit 0 on a healthy fixture, exit 1 with the missing-restore message after deleting `obj/`. | `[ ]` | | |

## Phase 12: Release

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T12.1 | Release workflow: on tag, publish self-contained single-file for win-x64, win-arm64, osx-x64, osx-arm64, linux-x64, linux-arm64; attach to a GitHub release; pack and push the global tool. **Tests first:** a smoke job downloads each artifact and runs `--version`. | `[ ]` | | |
| T12.2 | README: what it is, install, `doctor`, `scan`, `run`, `report`, the skill install line. | `[ ]` | | |
| T12.3 | Rename from working name once decided (D14): namespaces, folder, `.codemuster/`, tool id. | `[x]` | claude 2026-09-10 | Name chosen before any code existed; docs and folder renamed to CodeMuster (D23). |

## Phase 13: Dogfood

| ID | Task | Status | Owner / Date | Notes |
|---|---|---|---|---|
| T13.1 | `doctor` on ToolbagCRM; fix whatever it reports until both mappers are `working`. | `[ ]` | | |
| T13.2 | Scan ToolbagCRM; record resolution rate, slice count, orphan count, top unresolved names in Notes. | `[ ]` | | |
| T13.3 | One lens (tenant scoping) on one subtree with `run --agent claude`; record tokens, wall time, findings, verify results. | `[ ]` | | Decides budget defaults and whether verify is good enough. |
| T13.4 | Read every finding by hand and record precision in Notes. Feed lens and budget changes back as tasks. | `[ ]` | | |

---

## Later (not MVP, in rough order)

| ID | Task | Status | Notes |
|---|---|---|---|
| L1 | GitHub Action: restore ledger from cache, `scan`, `run`, `report`, coverage as a check run. | `[ ]` | Part 1.5 of `idea.md`. |
| L2 | SARIF export for GitHub code scanning. | `[ ]` | |
| L3 | Frontend-to-backend join: link a TS API-client leaf to the C# endpoint slice for true end-to-end units. | `[ ]` | Ledger must not preclude it; `Edge.kind` gains `Http`. |
| L4 | Non-git folders (own walk + ignore rules; stat cache already exists). | `[ ]` | D13. |
| L5 | Python and Go mappers. | `[ ]` | |
| L6 | Shared, versioned lenses (tenancy, a11y, OWASP). | `[ ]` | |
| L7 | Hosted scheduled scans (Part 2 of `idea.md`). | `[ ]` | Only after L1 has users. |
| L8 | Monetization conversation. | `[-]` | Deferred by Tim (D22). Remind him after Milestone B. |
| L9 | npm wrapper package `codemuster`: postinstall downloads the platform binary from the GitHub release so `npx codemuster` works anywhere Node is. Placeholder 0.0.1 in `npm/` reserves the name. | `[ ]` | D23. Name reserved: `codemuster@0.0.1` placeholder published to npm by tim on 2026-09-10 (README only, public). Domain `codemuster.com` and GitHub org/repo also held. Next real release must be >= 0.1.0. Still to reserve: NuGet id `CodeMuster` with the first tool push in T12.1. |
