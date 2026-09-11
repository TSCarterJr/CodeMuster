---
name: codemuster
description: Audit a whole repository with provable coverage using the codemuster CLI. Use when asked to audit, review, or security-check an entire codebase, or to continue or finish such an audit.
---

# CodeMuster

The CLI owns the state and the loop; you analyze one unit at a time, in this session, one after another. Run every command from inside the repository. Generated files, lockfiles, migrations, and binaries are excluded on purpose, so the unit count is lower than the file count.

1. `codemuster init --yes` once per repo (safe to repeat), then `codemuster scan`.
2. `codemuster estimate` gives a rough lower bound on token cost before you start.
3. Loop until `codemuster next` prints `nothing pending`:
   - `codemuster next` prints one unit pack. Read it whole.
   - The pack is the scope. You may read more of the repository for context, but every finding must cite a path listed under Files in the pack.
   - Write your response as JSON to a temporary file outside the repository, in exactly this shape:

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

   - A pack whose header says `- kind: verify` asks you to refute one earlier finding instead of auditing. Approach it as a skeptic who did not write the finding: check the claim against the code, and answer in exactly this shape, where `verdict` is confirmed, refuted, or unsure:

```json
{
  "verdict": "refuted",
  "reason": "Line 19 filters on TenantId before the Status filter, so the query is already scoped to the caller's tenant."
}
```

   - Run the `codemuster done ...` command printed at the end of the pack, pointing `--findings` at that file. If `done` rejects the response, fix what it names and run it again.
4. `codemuster status` shows coverage at any time; `codemuster report` renders findings and coverage as markdown once everything is analyzed. Every finding you record queues a verify pack, and refuted findings stay out of the report unless you add `--include-refuted`.

Rules: never skip a unit or invent findings for code you did not read; `summary` is one line about the unit; `severity` is critical, high, medium, low, or info; `category` is a short free-form label such as security, correctness, reliability, or performance; `confidence` is 0 to 1; an empty `findings` array is a valid, common answer.

Headless alternative: `codemuster run --agent claude -j 4` drives the same loop without you.
