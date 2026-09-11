---
name: codemuster
description: Audit a whole repository with provable coverage using the codemuster CLI. Use when asked to audit, review, or security-check an entire codebase, or to continue or finish such an audit.
---

# CodeMuster

The CLI owns the state and the loop; you analyze one unit at a time in a fresh context. Run every command from inside the repository.

1. `codemuster init --yes` once per repo (safe to repeat), then `codemuster scan`.
2. `codemuster estimate` tells you the approximate token cost before you start.
3. Loop until `codemuster status` shows every unit analyzed:
   - `codemuster next` prints one unit pack. Read it whole.
   - The pack is the scope. You may read more of the repository for context, but every finding must cite a path listed under Files in the pack.
   - Write your response as JSON to a temporary file, in exactly this shape:

```json
{
  "summary": "Repository for quotes; every query is expected to be scoped to the caller's tenant.",
  "findings": [
    {
      "path": "src/MixedRepo.Api/Data/QuoteRepository.cs",
      "line_start": 18,
      "line_end": 21,
      "severity": "high",
      "category": "security",
      "claim": "ListForTenant returns quotes for every tenant because the query has no TenantId filter.",
      "evidence": "The Where clause filters on Status only; TenantId is never referenced.",
      "confidence": 0.9,
      "lens_id": "default"
    }
  ]
}
```

   - Run the `codemuster done ...` command printed at the end of the pack, pointing `--findings` at that file. If `done` rejects the response, fix what it names and run it again.
4. `codemuster report` renders findings and coverage as markdown once status is complete.

Rules: never skip a unit or invent findings for code you did not read; `summary` is one line about the unit; `severity` is critical, high, medium, low, or info; `confidence` is 0 to 1; an empty `findings` array is a valid, common answer.

Headless alternative: `codemuster run --agent claude -j 4` drives the same loop without you.
