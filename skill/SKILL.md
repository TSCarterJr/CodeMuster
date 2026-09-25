---
name: codemuster
description: Use CodeMuster proactively during implementation, bug fixes, and refactoring according to repository settings. Audit repositories, review UI usability and unused-code candidates when enabled, verify findings, and recover CLI fixes.
---

# CodeMuster

For automatic use, apply the settings and worker checks below before these bootstrap commands. The CLI owns the ledger and work queue. Run commands inside the repository. For existing findings or failed fixes, start with Outcomes and recovery; preserve their diagnostics before starting a new audit. Use `codemuster <command> --help` for options. First run `codemuster --version`. Only if the command is not found, try `npm i -g codemuster` once (use `npm.cmd` on Windows), then run `codemuster --version` again before continuing. If installation is blocked or fails, npm is unavailable, or the CLI still cannot run, stop, explain the error, and tell the user to run `npm i -g codemuster` in their terminal (Node.js 22+ with npm is required), then reopen their terminal or agent session if the command is still not found. If an existing CLI returns another error, report it instead of reinstalling. This skill requires a stable CLI 0.2.7 or later. For an older version, run `codemuster update` once, then run `codemuster --version` again; if still incompatible or the version cannot be established, stop and explain the required upgrade. Respect an explicit `CODEMUSTER_VERSION` pin: explain the conflict instead of clearing it. Check `codemuster fix --help` for `--retry-declined` and `--include-related`, and `codemuster validate --help` before using them; missing capabilities mean stop. Refresh standalone skill copies with `codemuster skill install --for <agent>` after upgrading.

## Automatic use during coding

Read `.codemuster/config.json` before automatic CLI setup or work, at the completion checkpoint, and before each review or repair phase; stop any phase the current mode no longer permits. Its `automation` setting controls this workflow: `off` means no automatic CodeMuster actions; `update` means scan to refresh coverage only; `review` means scan, review affected units, verify findings, and report without CodeMuster repairs; `review_and_fix` adds CLI repairs, independent verification, and configured final validation. Missing `automation` defaults to `update`; invalid settings must be corrected rather than guessed or overwritten. Explicit user instructions take precedence. An explicit CodeMuster request can still run when automatic use is off. In a CodeMuster worker (`CODEMUSTER_WORKER=1`), follow the supplied work pack and do not start this automatic workflow.

For an enabled mode, use CodeMuster without waiting for the user to name it. If repository config is missing, follow the CLI bootstrap above and initialize with the appropriate Audit setup command; preserve existing config and do not select review or fix on the user's behalf. Track the paths changed by the current task, including new files; new files must be deliberately tracked before scan can cover them. At a meaningful checkpoint after edits and before reporting completion, re-read the mode and run `codemuster scan` once for that content state. Respect `exclude`, `verify`, `vulnerabilities`, lenses, and `test_command`. Update stops here with the actual refresh result; it does not review or repair.

For review modes, check `codemuster next --help` supports `--path`; if absent, explain the required CLI upgrade and do not substitute an unscoped audit. Review each affected path using `codemuster next --path <path>` and the exact `done` commands in its packs until nothing is pending. Follow Audit's response contracts below; shared units already completed are skipped. Use the active agent for this loop; headless `run --path <path>` is an alternative using the configured/chosen harness, model, effort, and concurrency. Keep routine work scoped to affected paths and their CLI-selected context; use a whole-repository audit when requested. If `verify` is disabled, review does not silently enable it.

`review_and_fix` is authorization for the existing fix/verify/validate workflow in affected scope, including local repair commits; do not ask the user to authorize that mode again. Confirm `test_command` covers the required checks before repairs. Isolated workers use committed source: when necessary, preserve this task's reviewed changes in a scoped local checkpoint commit before `fix`, without including pre-existing edits. Do not stash the changes being reviewed and then claim fixes against older source repair them. If task changes cannot be separated safely, or validation/agent setup is unavailable, report the concrete blocker. Verify candidate findings before fixing even if automatic verify is disabled; do not alter the setting. Use `fix --path <path>`, then `verify --path <path> --force`, and `validate`; follow Outcomes and recovery below. Never push from this workflow. Report unresolved findings and actual checks, and avoid repeating a failed automatic cycle without new evidence or changes.

## Audit

1. `codemuster init --for codex --yes` (use the user's selected agent, or a comma-separated selection), then `codemuster scan`. When using this skill through a plugin, use `codemuster init --yes --no-skills` instead to avoid duplicate project skills; this also skips project change hooks, and do not combine `--for` with `--no-skills`. Otherwise init installs project skills and change hooks; `--no-gitignore`, `--no-hooks`, and `--no-skills` opt out. Reload agents and approve their hook trust prompt when needed. Generated files, lockfiles, migrations, binaries, and configured exclusions are intentionally outside coverage. If scan warns or coverage is incomplete, `codemuster doctor` explains the missing setup.
2. `codemuster estimate` gives a lower bound on input tokens. Analyze interactively below, or use `codemuster run --agent codex -j 4` with the user's chosen harness and concurrency.
3. Loop `codemuster next` until `nothing pending`. Read each whole pack. The pack is the scope: read related code for context, but every finding must cite a path listed under Files. Write the response to a temporary JSON file outside the repository:

```json
{
  "summary": "Repository for invoices; every query is expected to be scoped to the caller's tenant.",
  "findings": [
    {
      "path": "src/Billing/InvoiceRepository.cs",
      "line_start": 18,
      "line_end": 21,
      "severity": "high",
      "category": "security",
      "claim": "ListOpen returns invoices for every tenant because the query has no TenantId filter.",
      "evidence": "The Where clause filters on Status only; TenantId is never referenced.",
      "confidence": 0.9,
      "lens_id": "default"
    }
  ]
}
```

For `kind: verify`, try to refute the earlier claim against current code. An absent direct caller does not prove code is unreachable; dependency injection, reflection, routing, and public library APIs count as reachable. Browser findings also require the Application reviews workflow below. Answer confirmed, refuted, resolved, or unsure with an evidence-backed reason:

```json
{
  "verdict": "refuted",
  "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant."
}
```

Run the exact `codemuster done ... --findings <file>` command printed in the pack. Correct rejected responses using the reported reason. Never invent findings or skip units; when browser work is blocked, report it without looping the same failed attempt. An empty findings array is valid; summary is one line; severity is critical/high/medium/low/info; category is free-form except the reserved application-review categories below; confidence is 0 to 1.
4. `codemuster status` shows coverage. `codemuster report --include-refuted` shows findings and verification reasons, including refutations normally hidden by `report`.

## Application reviews

Respect `dead_code: true` and `user_experience.enabled: true` in repository settings; both default to off, so do not enable these features during ordinary automatic work. UX applies only to recognized UI files or configured `user_experience.include` globs, with `exclude` winning; backend-only repositories are not applicable. Use the configured `base_url` and verify the running app comes from this checkout. Check `codemuster next --help` lists `ux` before using enabled application reviews; otherwise stop and explain the required CLI upgrade.

Use `codemuster next --kind ux` in the active browser-capable host and follow the pack's exact `ux_review` receipt. Primary user flow comes first: judge whether the sequence makes sense for the user's task even when every control technically works. Inspect readability, graphical/rendering defects, clipping, button-click feel and pressed/loading feedback, typos and confusing labels, and validation/error recovery. Complete the mandatory `experience_checks` areas: `flow`, `validation_recovery`, `graphics`, `interaction_feedback`, and `text_quality`. Always inspect `flow` and `graphics`; only the other three areas may be not applicable, with an explicit reason. Record actual screenshots, `artifact_sha256`, `runtime_source_evidence`, routes, roles/states, themes/viewports, and browser-measured composited colors/font sizes; never guess contrast from pixels. Keep disabled, decorative, or logo text out of normative contrast samples and explain applicable exceptions. `done` validates artifacts/hashes and derives findings from measured failures and experience checks. Headless `run` leaves browser units pending without a model call. Do not install a browser or fabricate observations; if the host browser, app, login, state, or reliable measurements are unavailable, report incomplete UX and never treat source review as a UX pass.

HTTP endpoints are externally callable even without a repository caller. Static `dead_code` candidates and `ux_recommendation` findings are report-only even in `review_and_fix`; do not bypass the CLI by deleting or repairing them manually. An observed objectively illogical flow is `ux_workflow`, not automatically a subjective recommendation because it technically succeeds; tie the finding to the task, required information, state and business rules. Reserve recommendations for pure taste or unproven preferences. Confirmed current `ux_readability`, `ux_workflow`, `ux_rendering`, `ux_interaction`, and `ux_text` findings may use scoped `fix`, but revisit the running UI after repair. A confirmed, refuted or resolved UX verdict requires a fresh `ux_review` receipt from `codemuster next --kind verify` and the pack's `done` command; prepare/reopen verification through the existing `verify --force` workflow, whose headless run cannot supply browser evidence. Use unsure when observations cannot settle a finding.

Shared styles, sibling workflows and backend behavior affect the UI: any included source change can make prior UX evidence stale. Automatic `next --path` selects task scope; do not guess a broader authorization or claim that selection reviewed every affected screen. Report the tested targets/states and report remaining stale UI targets; complete broader browser coverage when requested. Keep screenshots available for later verification and preserve their privacy according to the repository's normal artifact handling.

## Outcomes and recovery

Read `codemuster status` and `codemuster report --include-refuted` to understand existing findings, verification evidence, fix/decline reasons, and failed attempts. Reports are diagnostic input: use `codemuster fix` to perform repairs, not direct edits driven by the report. Historical failures can remain after recovery; check current outcomes. Hooks flag tracked-content changes; `scan` refreshes coverage. Hooks do not run audits or mark anything fixed.

Use `codemuster verify --agent codex --force` to reassess known findings against current code, including previously refuted or declined items. `--path` narrows the checks. Changed or scan-retired verification packs are rebuilt without replacing the original finding. A verifier returns `confirmed` (still present), `refuted` (original claim was wrong), `resolved` (previous defect no longer present, with evidence), or `unsure`. For interactive verification, use the pack's `codemuster done` command with that JSON verdict and reason. Resolved automatically records fixed with its reason; a later confirmed regression reopens it. Do not label a false positive as a repaired defect.

When authorized to fix, configure `test_command` in `.codemuster/config.json` as a program/argument array that runs the repository's required build and tests (use an existing validation script for multiple steps). Run `codemuster fix --agent codex -j 4` with the user's chosen harness/concurrency. The CLI runs tests, makes local commits and records addressed IDs as fixed and declined IDs with reasons. Every unresolved finding must receive exactly one outcome. Preserve local work through the offered stash or authorized `--stash`; inspect restoration messages afterward. Before any agent call, `fix` runs `test_command` once on the unmodified tree. If it reports that the command cannot start, fails there, or changes tracked files, report that output and repair the suite or `test_command` (`codemuster validate` reproduces it); pass `--allow-failing-tests` only when the user confirms the suite is expected to fail until this repair lands.

For declines, inspect the reason and retry through `codemuster fix --agent codex --retry-declined --path src/file.ts -j 4`. Already-fixed findings are skipped. If callers, catalogs, or other related files must change, inspect which files are needed and use `codemuster fix --agent codex --retry-declined --path src/file.ts --include-related src/caller.ts,src/catalog.ts -j 1`. Related paths must be exact existing tracked files; this explicit allowlist permits one coherent repair and commit in an isolated worker. Do not bypass the guard, blindly apply a rejected checkout, or switch to manual repair just because a worker declined. Explain a blocker if the available CLI scope cannot safely express the required change.

Investigate failed-attempt diagnostics before retrying; retry packs carry the previous reason as evidence, not instructions. Agent errors, invalid responses, scope violations, and failing tests need different remedies. Correct the cause and retry unfinished work; do not blindly increase attempts. A declined finding is unresolved, and a done fix unit may contain declines. A fixed outcome records an accepted repair; it is distinct from independent verification or build success. Never edit ledger rows to clear the queue.

After fixing, run `codemuster verify --agent codex --force` and inspect remaining confirmed/unsure/declined outcomes; continue justified repairs through `fix`. Once repairs are complete, run `codemuster validate` even if the fix queue is empty. It fails when no test command is configured. Ensure the command actually covers the required build/tests, run any additional repository-required checks, and repair failures through scoped `fix` where tied to findings. Do not claim success from coverage counts or an empty queue. Finally `scan` and audit changed code when full refreshed coverage is required. Report actual validation results, resolution evidence, unresolved reasons, and any stale/incomplete coverage; never claim a push or deployment from local fix commits.
