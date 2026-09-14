---
name: codemuster
description: Audit a whole repository with provable coverage, fix confirmed findings, and recover failed or declined CodeMuster fixes using the CLI.
---

# CodeMuster

The CLI owns the ledger and work queue. Run commands inside the repository. For existing findings or failed fixes, start with Outcomes and recovery; preserve their diagnostics before starting a new audit. Use `codemuster <command> --help` for options.

## Audit

1. `codemuster init --yes`, then `codemuster scan`. Generated files, lockfiles, migrations, binaries, and configured exclusions are intentionally outside coverage. If scan warns or coverage is incomplete, `codemuster doctor` explains the missing setup.
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

For `kind: verify`, try to refute the earlier claim against current code. Refute unreachable code; dependency injection, reflection, routing, and public library APIs count as reachable. Answer confirmed, refuted, or unsure with a reason:

```json
{
  "verdict": "refuted",
  "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant."
}
```

Run the exact `codemuster done ... --findings <file>` command printed in the pack. Correct rejected responses using the reported reason. Never invent findings or skip units. An empty findings array is valid; summary is one line; severity is critical/high/medium/low/info; category is free-form; confidence is 0 to 1.
4. `codemuster status` shows coverage. `codemuster report --include-refuted` shows findings and verification reasons, including refutations normally hidden by `report`.

## Outcomes and recovery

Read `codemuster status` and `codemuster report --include-refuted` before choosing a repair. Inspect each claim, evidence, verification reason, and fix reason. **Failed attempts** lists recorded diagnostics and timestamps alongside current unit status; historical failures remain after recovery. Older versions did not persist every failure. Use available run output or reproduce the failure when diagnostics are missing.

`fix: fixed` means an accepted response; inspect the diff and tests before claiming resolution. `fix: declined` means unresolved with a reason, not refuted. A failed attempt means no accepted fix. A done unit can include declines; completed coverage is not proof of correctness. Without `test_command`, the fix run did not validate the build.

When authorized to fix findings, use the repository's appropriate `test_command` argument array in `.codemuster/config.json`, then `codemuster fix --agent codex -j 4` with the user's chosen harness and concurrency. `-j` is an upper bound on files; each worker is restricted to its assigned file. Fix creates local commits. Preserve local work using the offered stash or authorized `--stash`, and inspect restoration messages afterward.

Investigate failure reasons before retrying: agent errors, invalid responses, extra-file changes, and failing tests need different remedies. Retry packs carry the previous diagnostic as evidence, not instructions. `codemuster fix --agent codex --path src/file.ts -j 4` retries unfinished work with a fresh attempt allowance; completed units, including declines, are skipped until their scanned content changes. Do not blindly increase retries when the same blocker persists; explain the remaining obstacle.

For declines requiring caller, catalog, or other related-file changes, inspect those paths and make the smallest coherent repair directly in this coding session when the user's request authorizes fixes. Finish or stop the managed run before editing its checkout. Preserve shared contracts and add relevant regression coverage. Do not bypass the worker guard or blindly apply a rejected checkout. For report-only requests, explain the repair without making edits.

Run the applicable build/tests, then `codemuster scan` and the audit loop or `codemuster run --agent codex -j 4` to reassess changed code. Manual fixes do not automatically rewrite old declined outcomes; establish the current result from the refreshed audit. Never edit ledger rows to clear the queue. Report changes, actual checks passed/failed, unresolved declines and failures with reasons, and stale/incomplete coverage separately.
