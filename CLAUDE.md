@AGENTS.md

## Current status

Updated 2026-09-11. `MVP-Checklist.md` stays the source of truth; this is the short version.

- **Milestone A (file-mode v0) is reached.** Phases 0 to 6 are done and CI runs the whole loop on Ubuntu, Windows, and macOS.
- **Works today:** `init`, `scan`, `status`, `estimate`, `next`, `done`, `run --agent claude|codex|gemini|opencode|fake`, `report`, and `skill install --for <harness> [--global]`. Every file in a git repo is one unit; there are no call-graph slices yet.
- **Try it:** `dotnet publish src/CodeMuster.Cli -c Release -o <folder>`, put that folder on PATH, then run `codemuster init --yes`, `scan`, `run --agent claude -j 4`, and `report --out audit.md` inside a repo.
- **Next:** Phase 7 (Roslyn mapper) and Phase 8 (TypeScript mapper) in parallel, then slices in Phase 9. L10 holds small test-strength follow-ups.
- **Waiting on Tim:** the first file-mode pass on ToolbagCRM (`E:\source\toolbagcrm`). Do not run CodeMuster there until Tim has set that repo up.
