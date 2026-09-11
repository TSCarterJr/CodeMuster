@AGENTS.md

## Current status

Updated 2026-09-11. `MVP-Checklist.md` stays the source of truth; this is the short version.

- **Milestone B (slice mode) is reached with the fake agent.** Phases 0 to 9 are done except T7.6, which is deferred with its reason in the checklist. CI runs everything on Ubuntu, Windows, and macOS.
- **Works today:** `scan` maps C# with Roslyn and TypeScript with the compiler API, then builds one slice per entry point (controller actions, `Map*` handlers, background services, Next.js pages) holding every function on its call path, an orphan unit for unreached functions, and whole-file units for everything else. `scan --mode file` keeps the Milestone A behavior. `next` packs show each member's numbered lines under its class header and shrink far members to signatures past `slice_token_budget`. `status` counts per kind and says `complete` only when resolution meets `resolution_threshold` and no mapper failed.
- **Try it:** `dotnet publish src/CodeMuster.Cli -c Release -o <folder>`, put that folder on PATH, restore the target repo (`dotnet restore`, `npm ci`) so the mappers resolve calls, then run `codemuster init --yes`, `scan`, `estimate`, `run --agent claude -j 4`, and `report --out audit.md`.
- **Known gaps:** nothing limits a run to one subtree; slice mode has not run with a real agent; the `codex`, `gemini`, and `opencode` adapters have never launched their harness; entry points miss Razor Pages, Blazor, SignalR, gRPC, and minimal API lambdas.
- **Waiting on Tim:** whether to store the call graph in the ledger, whether the import-graph fallback mapper is still wanted (D08, D09), making the skill the main entry point, and auto-updates. The first ToolbagCRM pass (`E:\source\toolbagcrm`) waits until Tim has set that repo up.
- **Next:** Phase 10 (verify), Phase 11 (doctor), Phase 12 (release). L10 holds small test-strength follow-ups.
