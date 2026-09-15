## Stage 1: Record permitted use
**Goal**: Replace MIT with personal and internal company use terms, per Tim's request.
**Success Criteria**: License and decision record distinguish use from resale and explain the idea limitation.
**Tests**: Review license against the requested permissions and restrictions.
**Status**: Complete

## Stage 2: Ship consistent license metadata
**Goal**: Include the license in npm packages and .NET package metadata.
**Success Criteria**: Every staged npm package carries the root license and references it.
**Tests**: Failing-then-passing staging test using all six platform fixtures.
**Status**: Complete

## Stage 3: Verify and document
**Goal**: Verify local packaging and record the task status.
**Success Criteria**: Local checks pass; publication status is explicit.
**Tests**: npm tests, dotnet test, diff checks.
**Status**: Complete

Local verification: all 16 npm tests, full dotnet test suite, and git diff --check passed on 2026-09-14. The packaging test first failed on the missing LICENSE, then passed for the launcher and all six platforms. Staging/production publication has not been performed; retain this plan until release verification.

## Stage 4: Specify skill setup
**Goal**: Record Tim's CLI installation fallback and its instruction contract.
**Success Criteria**: Decision recorded and regression test fails before implementation.
**Tests**: Targeted SkillTests.
**Status**: Complete

## Stage 5: Implement and distribute setup instructions
**Goal**: Update the bundled skill, user documentation, and both local installed copies.
**Success Criteria**: Missing CLI triggers one install attempt with verification and manual fallback.
**Tests**: Skill contract and embedded-resource tests; installed-copy hashes.
**Status**: Complete

## Stage 6: Verify skill setup
**Goal**: Run local checks and document limits.
**Success Criteria**: Full local suite passes and AGENTS.md records the result.
**Tests**: dotnet test, npm tests, formatting and diff checks.
**Status**: Complete

SK1 local verification: regression test failed before implementation; all 800 .NET tests and 16 npm tests pass with zero skips. Formatting and diff checks pass. Both installed skill copies match the source. Fresh-machine agent installation and release publication remain unverified.

## Stage 7: Marketplace package contract
**Goal**: Specify one generated plugin and two catalogs without changing the CLI runtime.
**Success Criteria**: D45 records the decision; staging tests fail before implementation.
**Tests**: Node tests for self-contained bundles, version alignment, drift detection, and preservation.
**Status**: Complete

## Stage 8: Distribution and onboarding
**Goal**: Generate plugin artifacts, integrate release/CI, and lead docs with AI installation.
**Success Criteria**: Both manifests/catalogs validate; CLI and standalone skill installation remain documented.
**Tests**: Official validators, artifact staging, npm tests, docs command checks.
**Status**: Complete

## Stage 9: Local verification and release handoff
**Goal**: Verify install/discovery locally and prepare concrete marketplace submission guidance.
**Success Criteria**: Full local suites pass; actual installs and unpublished/unreviewed state are distinguished.
**Tests**: Isolated harness installation, full dotnet and npm suites, syntax/format/diff checks.
**Status**: Complete

DIST1 local verification: initial staging tests failed because the generator did not exist, then passed. All 800 .NET and 22 npm tests pass; both platform validators and isolated marketplace installs pass. Archive contents and installed skill hashes match source. Prepared plugin version 0.2.1. No commit, push, hosted release, official submission, or live reviewer evaluation performed. Retain this plan for release verification.

## Stage 10: Preserve work and synchronize main
**Goal**: Fast-forward main without losing distribution changes or older stashes.
**Success Criteria**: HEAD matches fetched main; local work is restored and conflicts resolved.
**Tests**: Git divergence, unmerged-path and inventory checks.
**Status**: Complete

## Stage 11: Reconcile the current agent workflow
**Goal**: Retain main's setup/recovery/validation behavior and update distribution metadata.
**Success Criteria**: D42/D43 remain upstream decisions; local decisions use D44/D45; plugin init avoids duplicate skills; prepared version is 0.2.7.
**Tests**: Failing-then-passing skill integration contract and regenerated bundle checks.
**Status**: Complete

## Stage 12: Verify the combined checkout
**Goal**: Validate the synchronized source, package copies, and retained work.
**Success Criteria**: Full tests and plugin validators pass; no conflict markers or active Git operations; preservation stash remains available.
**Tests**: Full .NET/npm suites, isolated init/plugin checks, format/syntax/diff checks.
**Status**: Complete

Main-sync verification: HEAD and live origin/main match d65558c15181a39c184207b9de9325664a7881ca. All 815 .NET and 22 npm tests pass with no skips. A real init smoke exposed the incompatible --for/--no-skills combination; the skill now uses init --yes --no-skills without --for, and its regression executes that exact command against a fixture. Final 0.2.7 bundles pass both validators and fresh isolated installations; all six skill copies match. Recovery stash e711f0f8b3ef000dba5384ede812d333743b4ac6 and the prior stash are retained. Nothing committed or pushed.

## Launch remediation (Tim's requested priority, 2026-09-14)

## Stage 13: Safe repair execution
**Goal**: Unify fix isolation, enforce validation scope, preserve failed integration evidence, bound packs, and prevent concurrent coordinators.
**Success Criteria**: R1/R2 regressions fail first then pass; cancellation and recovery remain covered.
**Tests**: Application fakes and real CLI/Git fault tests, full .NET suite.
**Status**: Complete

## Stage 14: Compatible installation and adapters
**Goal**: Require a compatible CLI, provide deterministic rollback and architecture-specific caches, and enforce/document adapter permissions and supported ecosystems.
**Success Criteria**: Upgrade/pin/cache/skill/adapter regressions pass; bundled skill copies match.
**Tests**: npm tests, adapter and skill tests, isolated installation and live acceptance where available.
**Status**: Complete

## Stage 15: Enforced release pipeline
**Goal**: Isolate PR execution, require exact-revision full tests and package smokes, make publishing resumable, and verify publisher settings.
**Success Criteria**: Release cannot publish failed revisions; matching existing packages can be safely resumed; external checks recorded precisely.
**Tests**: Workflow contracts, registry/publishing tests, package rehearsal, GitHub/npm readback.
**Status**: In Progress

## Stage 16: Acceptance and handoff
**Goal**: Reconcile every readiness item and current status with fresh evidence.
**Success Criteria**: All local gates pass; external release/marketplace/website limitations remain explicit.
**Tests**: Full suites, format/security/parity checks, artifact/upgrade smokes and live scenarios.
**Status**: In Progress


LIVE1 verification: 831 .NET tests (237 Domain, 240 Application, 161 Infrastructure,
40 C# mapper, 15 TS mapper, 138 CLI) and 28 npm tests pass with zero skips. Full
formatting, strict Claude plugin validation, generated/copy parity, diff and NuGet
advisory checks pass. Corrected an existing import-order failure in
RunProgressWriterTests. Final packaged Windows 0.2.7 fixture smoke passes. Actual
0.2.0 ledger upgrade and binary pin/unpin passed. Live Claude/Codex repair,
configured tests, resolved verification and final validation passed in disposable
fixtures. An overlapping live process initially locked build output; that failed
build was discarded and the full suite passed after it exited.

Stages 15/16 remain In Progress for authenticated npm trusted-publisher inspection
(E401; Tim asked to log in), hosted candidate CI/package rehearsal and published
fresh-plugin acceptance. Live GitHub branch protection and external-fork approval
settings were changed and read back. Workflow changes are local until pushed.
Nothing committed, pushed, tagged, published or submitted to official directories.

## Setup and hook utilization review (Tim's requested priority, 2026-09-15)

## Stage 17: Synchronize and preserve the checkout
**Goal**: Fetch main and integrate available changes while preserving local work.
**Success Criteria**: HEAD matches fetched origin/main; local files and older stashes remain intact.
**Tests**: Fast-forward check, divergence, unmerged paths, operation state, and diff checks.
**Status**: Complete

## Stage 18: Establish setup and hook behavior
**Goal**: Trace setup, plugin integration, hook execution, and agent compatibility.
**Success Criteria**: Explain current behavior with source evidence and distinguish documented support from live execution.
**Tests**: Existing setup/skill/change-tracking tests and a disposable real CLI/Git smoke.
**Status**: Complete

## Stage 19: Propose the utilization model
**Goal**: Document a practical default and the implementation gaps needed to support it.
**Success Criteria**: Review separates current D42 behavior from proposed checkpoint reminders and optional automatic review.
**Tests**: Reconcile independent source/documentation reviews, command references, and observed results.
**Status**: Complete

HOOK1 verification: main remains d65558c, matching origin/main. The 41 selected
tests pass (24 Application, 1 Infrastructure, 16 CLI), with zero skips. A disposable
fixture confirms stale warnings without hook installation and a staging-only false
warning. See docs/setup-hooks-review.md for evidence and the proposal. Context7
was unavailable; compatibility was checked against official vendor documentation.
Only review/status documentation changed. No product behavior, agent settings,
commit, push, or release was changed; live hook acceptance was not performed.

HOOK1 clarification: Tim wants proactive use during ordinary coding, without a
separate CodeMuster request. The review now specifies the responsibilities of
standing instructions, skill discovery, session-context hooks, change signals,
and CLI-owned freshness/review. Reminder-only behavior does not meet the goal.
The implementation and automatic-repair scope remain separate from this completed
investigation; no plugin runtime or skill behavior was changed by the clarification.

## Settings-driven proactive plugin (HOOK2, 2026-09-15)

## Stage 20: Configuration and scoped review
**Goal**: Represent automatic behavior in repository settings and support scoped interactive packs.
**Success Criteria**: Four modes round-trip; invalid values fail clearly; existing configs default to update; next --path selects affected work.
**Tests**: Config and real CLI path-scope regressions, failing before implementation.
**Status**: Complete

## Stage 21: Plugin context hooks
**Goal**: Surface the configured workflow automatically in Claude and Codex sessions.
**Success Criteria**: Packaged session/edit hooks read current settings, support first-use setup, remain quiet when off, and avoid recursive worker invocation.
**Tests**: Real Node hook-process fixtures, generated package/archive parity, worker environment regression.
**Status**: Complete

## Stage 22: Shared instructions and setup documentation
**Goal**: Teach ordinary coding use and settings-based review/fix authority through the authoritative skill.
**Success Criteria**: Plugin and standalone copies agree; update/review/review-and-fix remain distinct; actual commands and local commit behavior are clear.
**Tests**: Existing skill contracts, independent scenario review, validators and command smokes.
**Status**: Complete

## Stage 23: Verify the combined implementation
**Goal**: Validate source, generated artifacts, and deterministic end-to-end behavior while preserving prior work.
**Success Criteria**: Full local suites and formatting/parity checks pass; live host and publication limits are stated precisely.
**Tests**: Full dotnet/npm suites, fixture workflow checks, plugin/skill validators, formatting and diff checks.
**Status**: Complete

HOOK2 local verification: 31 new .NET cases failed before implementation; the final
full suite passes all 862 tests (237 Domain, 257 Application, 171 Infrastructure,
40 C# mapper, 15 TS mapper, 142 CLI), zero skips. The 40 npm tests also pass with
zero skips, including mode changes, invalid configuration, missing CLI/config,
worker suppression, filesystem directory identity, and generated Windows shell
commands. Hook regressions failed first. The final .NET receipts are retained in
ignored TestResults/automation-final-20260915/.

An independent review found Windows path-casing and SQL wildcard scope problems;
both are fixed and regression-tested. Generic reviews exclude Fix units and do
not restale completed repairs; explicit fix queues still work. Full formatting,
skill/plugin validators, generator parity, installed-copy hashes, Markdown links,
and diff checks pass. Actual Claude/Codex tool-triggered plugin acceptance,
hosted OS matrix, and publication remain outside this local evidence. No commit,
push, release, or user-profile plugin installation was performed. Earlier launch
stages remain open; retain this plan for staging/production verification.

## Application-aware reviews (REVIEW1, 2026-09-15)

## Stage 24: Settings and review planning
**Goal**: Plan opt-in dead-code candidates and separate UI-only UX units through existing scan/queue APIs.
**Success Criteria**: Disabled settings preserve existing behavior; backend-only code creates no UX units; exclusions and changes invalidate the appropriate work.
**Tests**: Failing config, scope, scan, and real CLI regressions before implementation.
**Status**: Complete

## Stage 25: Reachability and repair safeguards
**Goal**: Report unused internal candidates without deleting externally callable or uncertain code.
**Success Criteria**: HTTP/browser paths and framework/public entry points remain protected; low-fidelity maps state uncertainty; automatic repair excludes candidates and design recommendations.
**Tests**: Pure graph/source fixtures plus managed fix regressions with misleading confirmed findings.
**Status**: Complete

## Stage 26: Browser evidence and honest completion
**Goal**: Require rendered readability, sensible task flow and experience checks for UX completion and retain the evidence across sessions.
**Success Criteria**: Missing, stale, incomplete, or blocked browser receipts cannot complete UX; contrast, flow, validation, rendering, interaction and wording issues become findings; definitive verification also requires browser evidence; reports expose evidence and missing checks.
**Tests**: Evidence validation, real SQLite upgrade/reopen, next/done/status/report, and CLI integration regressions.
**Status**: Complete

## Stage 27: Plugin workflow, documentation, and validation
**Goal**: Teach active agents the scoped review/evidence process and verify the combined implementation.
**Success Criteria**: Shared/generated/installed skills agree; full local suites and formatting pass; actual browser/provider and publication limits are stated.
**Tests**: Skill contracts, generated plugin parity, full .NET/npm suites, formatting, and disposable CLI acceptance.
**Status**: Complete

REVIEW1 local verification: all 1,094 .NET tests pass (237 Domain, 468 Application,
177 Infrastructure, 40 C# mapper, 15 TypeScript mapper, 157 CLI), zero skips.
The 40 npm tests, full formatting, Claude strict plugin validation, all 11 generated
plugin files, four identical skill hashes, and diff checks pass. Tests demanded
the new behavior before implementation. Final instruction review corrected the
browser requirement for refuted verdicts and disallowed not-applicable flow/graphics;
the corresponding stale skill-text assertion was updated and the entire CLI suite
rerun successfully. Retain both failed and passing receipts under ignored
TestResults/review1-final-20260915/ and TestResults/review1-complete-20260915/.

An actual browser fixture reproduced invoice text at 1.94:1 contrast, payment only
on Schedule, the resulting invoice-task detour, and absent pressed-button feedback
despite a working click. The CLI generated the recorded findings from browser
receipts with empty model findings arrays. A deliberately wrong screenshot hash
was rejected and left 0/2 UI targets with evidence; valid screenshots and receipts
recorded 2/2. Source work and four independent finding verifications remained pending;
this is not a whole-repository audit or repair claim. Browser screenshots, computed
observations, source correspondence, reports and rejection receipts remain under
TestResults/ux-browser-acceptance-20260915/. The isolated server was stopped after
matching its process identity. No packages were installed for browser acceptance.

Implementation is complete locally. Actual proactive Claude/Codex host acceptance,
representative application flow and repair/reverification, hosted OS matrix and
release remain pilot/release gates. No root-repository commit, push, publication,
or user-profile plugin installation was performed. Existing work and both stashes
are preserved. Retain this plan until the remaining staging/production gates pass.

## Candidate source integration (INT1, 2026-09-15)

Tim explicitly requested committing and pushing the completed work to main. INT1
packages the previously validated dependency set: shared plugin distribution,
isolated repair and release safeguards, proactive settings/hooks, and application
reviews. Shared source/test hunks make independent historical commits require code
reconstruction; this integration task preserves the tested combined implementation.
The existing 1,094 .NET results were reconciled and all 40 npm tests rerun successfully.
Formatting, plugin parity/strict validation, diff checks and NuGet advisory checks
pass. Only reviewed source, docs, tests and generated plugin/skill artifacts belong
in the commit; ignored evidence and both existing stashes remain local. Protected
main integration requires Linux, Windows and macOS checks. Source delivery does not
publish a package or complete the remaining live-agent and release acceptance gates.
