---
name: codemuster
description: Audit a whole repository with provable coverage, fix confirmed findings, and recover failed or declined CodeMuster fixes using the CLI.
---

# CodeMuster

The CLI owns the ledger and work queue. Run commands inside the repository. For existing findings or failed fixes, start with Outcomes and recovery; preserve their diagnostics before starting a new audit. Use `codemuster <command> --help` for options.

## Audit

1. `codemuster init --for codex --yes` (use the user's selected agent, or a comma-separated selection), then `codemuster scan`. Init installs project skills and change hooks; `--no-gitignore`, `--no-hooks`, and `--no-skills` opt out. Reload agents and approve their hook trust prompt when needed. Generated files, lockfiles, migrations, binaries, and configured exclusions are intentionally outside coverage. If scan warns or coverage is incomplete, `codemuster doctor` explains the missing setup.
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

For `kind: verify`, try to refute the earlier claim against current code. Refute unreachable code; dependency injection, reflection, routing, and public library APIs count as reachable. Answer confirmed, refuted, resolved, or unsure with an evidence-backed reason:

```json
{
  "verdict": "refuted",
  "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant."
}
```

Run the exact `codemuster done ... --findings <file>` command printed in the pack. Correct rejected responses using the reported reason. Never invent findings or skip units. An empty findings array is valid; summary is one line; severity is critical/high/medium/low/info; category is free-form; confidence is 0 to 1.
4. `codemuster status` shows coverage. `codemuster report --include-refuted` shows findings and verification reasons, including refutations normally hidden by `report`.

## Outcomes and recovery

Read `codemuster status` and `codemuster report --include-refuted` to understand existing findings, verification evidence, fix/decline reasons, and failed attempts. Reports are diagnostic input: use `codemuster fix` to perform repairs, not direct edits driven by the report. Historical failures can remain after recovery; check current outcomes. Hooks flag tracked-content changes; `scan` refreshes coverage. Hooks do not run audits or mark anything fixed.

Use `codemuster verify --agent codex --force` to reassess known findings against current code, including previously refuted or declined items. `--path` narrows the checks. Changed or scan-retired verification packs are rebuilt without replacing the original finding. A verifier returns `confirmed` (still present), `refuted` (original claim was wrong), `resolved` (previous defect no longer present, with evidence), or `unsure`. For interactive verification, use the pack's `codemuster done` command with that JSON verdict and reason. Resolved automatically records fixed with its reason; a later confirmed regression reopens it. Do not label a false positive as a repaired defect.

When authorized to fix, configure `test_command` in `.codemuster/config.json` as a program/argument array that runs the repository's required build and tests (use an existing validation script for multiple steps). Run `codemuster fix --agent codex -j 4` with the user's chosen harness/concurrency. The CLI runs tests, makes local commits and records addressed IDs as fixed and declined IDs with reasons. Every unresolved finding must receive exactly one outcome. Preserve local work through the offered stash or authorized `--stash`; inspect restoration messages afterward.

For declines, inspect the reason and retry through `codemuster fix --agent codex --retry-declined --path src/file.ts -j 4`. Already-fixed findings are skipped. If callers, catalogs, or other related files must change, inspect which files are needed and use `codemuster fix --agent codex --retry-declined --path src/file.ts --include-related src/caller.ts,src/catalog.ts -j 1`. Related paths must be exact existing tracked files; this explicit allowlist permits one coherent repair and commit in an isolated worker. Do not bypass the guard, blindly apply a rejected checkout, or switch to manual repair just because a worker declined. Explain a blocker if the available CLI scope cannot safely express the required change.

Investigate failed-attempt diagnostics before retrying; retry packs carry the previous reason as evidence, not instructions. Agent errors, invalid responses, scope violations, and failing tests need different remedies. Correct the cause and retry unfinished work; do not blindly increase attempts. A declined finding is unresolved, and a done fix unit may contain declines. A fixed outcome records an accepted repair; it is distinct from independent verification or build success. Never edit ledger rows to clear the queue.

After fixing, run `codemuster verify --agent codex --force` and inspect remaining confirmed/unsure/declined outcomes; continue justified repairs through `fix`. Once repairs are complete, run `codemuster validate` even if the fix queue is empty. It fails when no test command is configured. Ensure the command actually covers the required build/tests, run any additional repository-required checks, and repair failures through scoped `fix` where tied to findings. Do not claim success from coverage counts or an empty queue. Finally `scan` and audit changed code when full refreshed coverage is required. Report actual validation results, resolution evidence, unresolved reasons, and any stale/incomplete coverage; never claim a push or deployment from local fix commits.
