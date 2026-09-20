# Agent instructions for CodeMuster

## Current task status

- 2026-09-19, codex: CLI1 (D54) adds a compact ASCII terminal wordmark,
  emphasized provider/model/thinking/worker rows, countdown bar, colored audit
  progress and elapsed intelligent-config activity with success/error/cancel states.
  NO_COLOR preserves layout without colors; CI, redirection and TERM=dumb use
  plain output without animation. Existing model selection and stdout formats
  remain intact. Regressions failed first; all 1,241 .NET tests pass, with 27
  focused cases rerun after the final foreground-color adjustment. Real terminal
  fixtures verify layout/colors, failure/cancellation cleanup, countdown/Enter,
  unchanged config/ledger, narrow windows and fallback behavior. Formatting/diff
  checks pass. Evidence: TestResults/cli1/ and /tmp/codemuster-cli1-*. Existing
  untracked setup/config files remain untouched. Source only; no release yet.

- 2026-09-19, codex: REL030 published npm/plugin 0.3.0 from protected-main
  merge d43cb8f (PR #7), immutable tag v0.3.0. Release run 35473378759 passed
  three-OS validation, six builds, three package smokes and publication. All seven
  public npm versions/latest tags and SHA512 integrities, and all nine GitHub
  asset sizes/SHA256 digests match the original archives. Fresh macOS arm64
  registry installation passes the complete CLI workflow and intelligent-config
  apply/backup/no-op/rejected-setting checks; the new updater downloads 0.3.0 and
  displays its release notes. Normal --version reports up-to-date on stderr.
  Final local validation: 1,223 .NET and 61 npm tests, zero skips; format/plugin
  parity/diff checks pass. Evidence is retained under TestResults/release-0.3.0/.
  Upgrade older npm launchers with npm install -g codemuster@0.3.0 once, because
  their binary-only update cannot replace the launcher JavaScript. No live AI
  recommendation-quality acceptance was performed.

- 2026-09-19, codex: UPDATE1, START1 and CONFIG1 are implemented locally under
  D51-D53 at Tim's request. A packaged CHANGELOG supplies intervening release
  notes after updates; normal launcher commands report npm availability with a
  two-second timeout while preserving daily background installs and opt-outs.
  Agent commands show provider/model/effort and worker limit, with a ten-second
  terminal countdown (Enter starts; Esc/Ctrl+C cancels; CI/redirection skips).
  intelligent-config defaults to Codex, uses one read-only call over bounded repo
  context, and applies validated additive exclusions/lenses and detected test setup.
  Existing settings survive; exact backups, atomic config replacement, no-op and
  concurrent-edit guards are covered. The command does not open the ledger.
  New CLI/launcher regressions failed before implementation. All 1,223 .NET and
  61 npm tests pass; the full run's xUnit style error was corrected and the full
  Application suite rerun. Formatting, syntax and diff checks pass. Real terminal
  fixtures verified countdown/start/cancel, unchanged ledger/config on cancellation,
  and CI bypass. All seven staged npm packages include CHANGELOG.md. Receipts are
  under ignored TestResults/start1/ and TestResults/config1/, with test logs under
  /tmp/codemuster-config1-*. Tim authorized 0.3.0 delivery under REL030. Release
  preparation includes the dated changelog and plugin version; hosted validation
  and publication remain pending. No live AI recommendation call was made. Older
  launchers require npm installation to gain the new JavaScript update features.

- 2026-09-19, codex: REL029 published npm/plugin version 0.2.9 at Tim's explicit
  request. Protected-main PR #5 merged as 1888754; immutable tag v0.2.9 points
  to that commit. Release run 35469006600 passed all three OS test jobs, six
  platform builds, three installed-package smokes, npm publication and GitHub
  release creation. All 1,192 .NET and 40 npm tests pass. All seven npm versions
  and latest tags are 0.2.9; SHA512 integrities match the original archives.
  All nine GitHub assets match by size and SHA256. A fresh macOS arm64 npm
  install passes the packaged CLI workflow and a 0.2.8 ledger upgrade fixture:
  preserved history, excluded documents/data, persistent oversized skip,
  continued queue processing and successful retry after raising the budget.
  Retained receipts and original archives are under TestResults/release-0.2.9/;
  see docs/launch-readiness.md. Rescan existing projects after upgrading.

- 2026-09-19, codex: RUN2 (D50) extends RUN1 to document/data/configuration/database
  exclusions and persistent oversized skips. `run`, `verify`, and `next` skip
  oversized units once without agent attempts or retries; status/report retain the
  reason and never count skipped work as analyzed. Scan or force requeues skips.
  Mapper metadata and ecosystem dependency audits still receive their inputs.
  Schema 7 gates the new status without changing existing rows; next now takes
  the coordinator lock because it can persist skips. All 1,192 .NET and 40 npm
  tests pass after updating exclusion/schema expectations, including a targeted
  infrastructure rerun for the final schema assertion. Formatting and diff checks
  pass. Tim authorized commit/push and npm 0.2.9 publication on 2026-09-19.
  RUN2 includes the superseded RUN1 changes; release work is tracked as REL029.

- 2026-09-19, codex: RUN1 (D49) excludes `.md` documentation case-insensitively
  and isolates headless pack-construction failures to their unit. Other units
  continue; failed packs remain incomplete and retryable with a nonzero exit.
  Rescanning retires legacy Markdown units while preserving history. Regressions
  failed first; all 1,131 .NET and 40 npm tests pass, zero skips. Formatting and
  diff checks pass. Clean main was fast-forwarded to 213eb14 before this change.
  RUN2 superseded this behavior and shipped with npm 0.2.9 under REL029.

- 2026-09-15, codex: REL028 publishes version 0.2.8 for Tim's functionality testing.
  Release commit bfe1290 is on main and annotated tag v0.2.8 points to it. Fixed a
  macOS dead-code regex timeout with ordinal token checks and linear-time request
  matching, preserving Unicode boundaries and conservative incomplete-map results.
  All 1,119 .NET and 40 npm tests pass, zero skips; formatting, plugin validation,
  diff and NuGet advisory checks pass. Hosted run 35015077715 passed three-OS
  validation, six platform builds and three installed-package smokes. Its first
  publication failed with E404 because all six platform packages lacked trusted
  publishers. After npm login/2FA, configured GitHub TSCarterJr/CodeMuster,
  release.yml with direct-publish permission and verified all seven publishers.
  Attempt 2 reran only failed jobs and published the original archives. All nine
  GitHub release asset SHA256 digests match the retained bytes. All seven npm
  versions/latest tags and SHA512 integrities match. Windows ARM64 finished npm
  processing and was verified at 21:05 UTC. Fresh registry installation on Windows x64 reports 0.2.8,
  exposes UX/scoped-repair commands, and passes init, C#/TS scan, ledger reopen,
  audit, verification, isolated fix and configured fixture-build validation.
  Evidence is under ignored TestResults/release-0.2.8/. Do not move the tag or
  rebuild published versions. Live proactive host-plugin and representative UX
  repair/reverification remain separate pilot work. Both stashes are preserved.
- 2026-09-15, codex: INT1 integrates the completed candidate for Tim's explicit
  commit/push-to-main request. This snapshot includes the shared distribution and
  setup work, isolated repair/release safeguards, settings-driven proactive hooks,
  and evidence-backed dead-code/UX reviews. The prior tasks share implementation
  and test changes; the integration task keeps their tested dependency set together.
  Local validation remains 1,094 .NET and 40 npm passes with zero skips; formatting,
  plugin parity/strict validation, diff checks and NuGet advisory checks pass.
  Main requires Linux, Windows and macOS CI before integration; retain that
  protection. This source delivery does not publish packages or satisfy live
  proactive plugin, representative UI repair, or published-install acceptance.
  Both existing stashes and ignored browser/test/package evidence are retained.
- 2026-09-15, codex: REVIEW1 implemented locally under D48. Opt-in `dead_code`
  records conservative usage assessments while protecting HTTP/external entry
  points; candidates never authorize automatic deletion. Opt-in `user_experience`
  plans UI-only browser work with primary task flow, readability, graphics/rendering/
  clipping, pressed/loading feedback, wording/typos, and validation/recovery checks.
  Receipts require current source, screenshot hashes, measured contrast and explicit
  observations; source-only or blocked reviews cannot complete UX. Definitive UX
  verdicts also require browser evidence, and automatic repairs exclude stale
  observations and recommendations. SQLite schema 6 retains review evidence.
  A real browser fixture plus CLI ingestion recorded low contrast (1.94:1), missing
  invoice payment placement, task detours, and absent button-press feedback despite
  empty submitted findings; a wrong artifact hash was rejected and kept UX incomplete.
  All 1,094 .NET and 40 npm tests pass with zero skips; formatting, plugin strict
  validation, generated/installed copy parity and diff checks pass. Receipts are in
  ignored TestResults/review1-complete-20260915/ and ux-browser-acceptance-20260915/.
  See docs/application-reviews.md and stages 24-27. Actual proactive host-plugin
  acceptance, representative UI repair/reverification, hosted OS validation and
  publication remain release/pilot gates. Existing work and both stashes remain
  preserved; no repository commit, push or release. The disposable browser server
  was stopped after verifying its process identity; all evidence was retained.
- 2026-09-15, codex: Refreshed release readiness after HOOK2. Ready for a controlled
  pilot with matching candidate artifacts; public launch still needs actual proactive
  Claude/Codex session acceptance, exact committed-revision hosted CI/package rehearsal,
  all-package npm publisher verification and successful publication, then fresh published
  install/upgrade acceptance. Retained receipts confirm 862 .NET passes, zero skips;
  the latest local npm suite has 40 passes and generated plugin parity still passes.
  GitHub latest release and checked npm launcher/darwin-arm64 remain 0.2.0; latest
  hosted tests cover d65558c, excluding the uncommitted candidate. Updated
  docs/launch-readiness.md with current gates and expanded P1–P8/N1–N4 acceptance.
  No runtime changes, live-agent runs, commit, push, or publication in this reassessment.
- 2026-09-15, codex: HOOK2 implemented locally under D47. Repository `automation`
  settings select off, update (default), review, or review_and_fix. Claude/Codex
  plugin session/edit context hooks read current settings; the shared skill checks
  them again before review/repair and treats configured review_and_fix as authority
  for scoped local repairs. Added next --path, literal file/folder matching,
  review-queue exclusion of repair units, and CODEMUSTER_WORKER suppression.
  All 862 .NET and 40 npm tests pass with zero skips; new regressions failed first.
  Formatting, skill/plugin validators, generated/installed copy parity, Markdown
  links, and diff checks pass. Hook-process fixtures cover settings changes,
  bootstrap, invalid config, filesystem identity, and Windows CMD/PowerShell.
  Actual agent-triggered plugin acceptance and publication remain unverified.
  Existing local work and both stashes are preserved; no commit, push, or release.
- 2026-09-15, codex: Tim clarified HOOK1's intended product behavior: the plugin
  should teach CLI use and direct the AI to use CodeMuster proactively during
  normal coding, without a separate CodeMuster request. Updated the review with
  proposed standing instructions, broader skill activation, session-context hooks,
  and path-aware change signals. Current hooks record a repository fingerprint,
  not a per-file dirty list. CLI fingerprints and the ledger remain authoritative.
  This updates the design direction; proactive plugin behavior is not implemented.
- 2026-09-15, codex: Fetched and fast-forward checked main; HEAD and origin/main
  both remain d65558c, with existing local work and both stashes preserved.
  HOOK1 reviews setup and hook utilization in docs/setup-hooks-review.md.
  All 41 selected setup/skill/change-tracking tests pass with zero skips. A real
  disposable CLI/Git smoke confirms freshness warnings without installed hooks
  and a false warning after staging unchanged working-file content. Current
  hooks only record fingerprints; plugin setup skips them and has no hooks-only
  option. Official docs confirm the current Codex hook format; Claude's matcher
  omits PowerShell. Recommend checkpoint reminders, independent hook setup, and
  managed upgrade/removal before optional automatic reviews. This is a proposal;
  D42 behavior is unchanged. No production edits or agent settings changes;
  actual agent-triggered hook acceptance remains unverified.
- 2026-09-14, codex: npm step-up authentication succeeded for codemuster.
  Its trusted publisher is GitHub TSCarterJr/CodeMuster, release.yml, with
  createPackage/createStagedPackage permissions. Checking darwin-arm64 requests
  a separate EOTP challenge; the six platform publishers are not yet verified.
  Login is working, but hosted candidate validation and actual publication remain
  pending; this is not a completed launch gate.
- 2026-09-14, codex: npm login now verified as tscarterjr. Trusted-publisher
  inspection progressed from E401 to npm step-up authentication (EOTP). Started
  interactive trust inspection and opened the npm browser challenge; awaiting
  account authentication before publisher configuration can be read. No package
  publication or publisher configuration change performed.
- 2026-09-14, codex: LIVE1 launch remediation implemented locally under D46.
  Default and parallel fixes share isolated workers; validation scope is enforced,
  commits precede ledger success, integration failures preserve recovery, and
  mutating commands use a common-Git-directory coordinator lock. Packs render on
  worker acquisition and oversized whole-file packs fail explicitly. Skill checks
  stable CLI 0.2.7+ and capabilities; launcher supports exact version pins and
  architecture-partitioned caches. Added Codex/OpenCode/Gemini tool restrictions,
  Yarn Berry command/parser support, hosted-only workflow runners, exact-revision
  full-test release gates, packaged runtime smokes, and resumable npm publication.
  All 831 .NET and 28 npm tests pass with zero skips; full formatting, plugin
  parity/Claude strict validation, diff checks and NuGet advisory scan pass.
  Final self-contained Windows 0.2.7 package passed installed-runtime fixture
  smoke. Real published 0.2.0 ledger upgrade and exact binary rollback passed.
  Live Claude and Codex each repaired a real fixture defect with configured tests,
  verified resolution, and passed final validation. Live main protection now
  requires all three strict OS checks with admins enforced; fork approval is
  all_external_contributors. Existing work and both stashes remain intact.
  Code/workflows remain uncommitted and unpublished; hosted candidate tests,
  fresh published-plugin acceptance and live Gemini/OpenCode acceptance remain
  pending. npm 11 publisher inspection returned E401; requested npm login.
  See docs/launch-readiness.md for the reconciled evidence and release gates.
- 2026-09-14, codex: Completed setup/release-readiness review in
  `docs/launch-readiness.md`. All 815 .NET and 22 npm tests pass, plugin parity
  and Claude strict validation pass, and the NuGet advisory check is clean.
  Disposable real-CLI/Git checks reproduced serial fixes committing out-of-scope
  tracked edits and recording fixed before a rejected commit. Hold the next
  launch for these fixes, public-PR/self-hosted runner isolation, exact-revision
  release gates, and skill/CLI compatibility checks. Live npm/GitHub remains
  0.2.0; local plugin prepares 0.2.7. Latest tagged workflow failed publishing;
  current trusted-publisher authorization and live-agent acceptance remain
  unverified. Updated review/status documentation only; preserved existing work.
  No production-code edits, real-repository commits, push, release, or settings
  changes performed.
- 2026-09-14, codex: Synced main from `744132a` to `d65558c` (four commits), then
  reconciled all six instruction/documentation conflicts. Preserved upstream
  D42/D43 and the T16 setup, declined-fix recovery, resolved verification, and
  final validation workflow; local setup/distribution decisions are D44/D45.
  Plugin init uses `--no-skills` to avoid duplicate skills (also skips project
  hooks). Prepared plugin version is now 0.2.7, superseding the earlier 0.2.1
  artifacts after Tim reported 0.2.6 on his MacBook. All 815 .NET and 22 npm
  tests pass; plugin validators, isolated plugin updates, init smoke, copy hashes,
  formatting, syntax, and diff checks pass. HEAD matches origin/main, with no
  remaining conflicts. Local changes remain uncommitted; all 31 preserved files
  remain present. Recovery stash `e711f0f` and the older license stash are retained.
  No push or release performed; published GitHub/npm still reported 0.2.0, and
  the MacBook installation was not inspected.
- 2026-09-14, codex: DIST1 (D45) implements marketplace-first distribution with
  one generated plugin for Claude/Codex and the standalone CLI preserved. Source
  metadata is in `distribution/`; run `node scripts/stage-plugin.js` after changing
  it, the skill, or LICENSE. CI checks parity; tagged releases require matching
  plugin versions and attach plugin/skill archives. Prepared version 0.2.1 locally.
  All 800 .NET and 22 npm tests pass, as do manifest, formatting, syntax, YAML, and
  diff checks. Both agents installed matching skill copies in isolated profiles.
  Codex's temporary profile reported a helper-alias warning; plugin installation
  and listing succeeded. Local archives are in ignored `plugin-packages/`.
  Onboarding and `docs/marketplace-submission.md` cover release and directory review.
  No commit, push, hosted workflow, release, or official-directory submission was
  performed. Fresh-machine CLI bootstrap and live reviewer cases remain unverified.
- 2026-09-14, codex: SK1 (D44) adds skill-driven CLI setup: check version, try
  `npm i -g codemuster` once only when missing, verify, and explain failures with
  manual install instructions. Updated both local skill copies and user docs.
  Regression test failed before implementation; all 800 .NET tests and 16 npm
  tests now pass locally, with formatting and diff checks passing. Fresh-machine
  agent installation and package publication were not exercised.
- 2026-09-14, codex: Installed the current bundled CodeMuster skill for Claude and
  Codex in this repository at `.claude/skills/codemuster/SKILL.md` and
  `.codex/skills/codemuster/SKILL.md`. Both installed copies match `skill/SKILL.md`.
  Installation is repository-local; skill discovery in a new agent session has
  not yet been verified.
- 2026-09-14, codex: LIC1 replaces MIT with the CodeMuster Personal and Internal
  Business Use License at Tim's request (D41). Personal and internal company use,
  including private modifications, is permitted; resale, commercial forks, and
  paid services exposing CodeMuster are prohibited. The license does not claim
  exclusive rights over ideas or revoke earlier license grants.
- License metadata and release staging are updated. After syncing main, all 16 npm
  tests and the full dotnet test suite pass locally. Formatting, syntax, and diff
  checks pass; the NuGet dependency scan reports no known vulnerabilities.
  No package release has been published by this task.

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
distribution/                       authoritative plugin metadata and README (D45).
plugins/codemuster/                  generated plugin bundle; run scripts/stage-plugin.js.
.claude-plugin/ and .agents/plugins/ generated marketplace catalogs (D45).
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
- Do not auto-install dependencies. `doctor` prints commands. Exceptions: the npm launcher keeps CodeMuster itself up to date (D33), `init` installs user-selected project skills and hooks (D42), and the skill attempts `npm i -g codemuster` when the CLI is missing, with manual instructions on failure (D44).
- Do not commit `.codemuster/ledger.db` in any repo, including fixtures.
