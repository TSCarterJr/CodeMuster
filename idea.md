# CodeMuster: full-coverage codebase audit tool

Captured 2026-09-10 from a design conversation in the ToolbagCRM repo. Named CodeMuster on 2026-09-10 (see
Name research at the bottom).

## The problem

Asking Claude / Codex / Gemini to "audit the whole codebase" never touches every file and never
follows the flows. It is not a prompting problem. It is a capacity problem: the state of what has
and has not been analyzed lives inside a context window that cannot hold the repo.

Sizing on ToolbagCRM (tracked, hand-written only, 2026-09-10):

| | |
|---|---|
| C# files, excluding migrations and generated | 2,285 |
| TS/TSX files, excluding tests, e2e, `.d.ts` | 686 |
| Bytes of that code | ~30 MB |
| Rough tokens to read it all once | ~7.5M |

No session can hold that, so the model samples and pretends.

## The reframe

Flip who drives. A script iterates over every file and calls the model once per unit of work in a
fresh context, then checks the unit off. The state lives outside the context window so sessions
chain. The model cannot skip a file it was never asked to skip.

**The script is the outer loop. The model is the inner function.**

## Part 1: open-source CLI + skill

### Discovery
- `git ls-files` for the file list. It already honors `.gitignore` (no `node_modules`, `bin`,
  `obj` filtering needed).
- Extension map picks languages. Mixed repos get every matching language.
- Generated-file filter: `*/Migrations/*`, `*.g.cs`, `*.Designer.cs`, `*.min.js`, lockfiles,
  `*.d.ts`, snapshot files, and anything marked `linguist-generated` in `.gitattributes`.
- Tests are tracked but can be excluded per lens.

### Ledger (SQLite, stdlib)
- One row per file: path, language, blob SHA, analyzed_at, analyzed_at_commit, lens hash,
  model, findings.
- **Staleness key is the git blob SHA**, not mtime, not scan time, not last-touching commit.
  `git ls-files -s` gives every file's content hash in one process. Same hash = identical
  content = skip. "Last commit that touched it" costs one git process per file and is less
  precise; timestamps and commit are kept for reporting only.
- Lens hash and model are stored so changing either invalidates prior analyses.

### Commands
- `scan` builds/refreshes the ledger.
- `doctor` lists language servers found on PATH with install commands for missing ones.
  Prints the command, never auto-installs.
- `next [--batch N]` prints the next unanalyzed unit(s) with context pack.
- `done <path> --findings <file>` records findings and the blob SHA analyzed.
- `status` prints coverage: N/M analyzed at commit X, K stale.
- `report` renders markdown or SARIF.
- `run --agent claude|codex|gemini -j N` is the headless driver. All three CLIs have headless
  modes (`claude -p`, `codex exec`, `gemini -p`). The script shells out per unit; each unit gets
  a full fresh context window. Ctrl-C and resume are free because the ledger is the state.
- `estimate` prints approximate token cost before a run (bytes / 4).

### LSP and following flows
- Do NOT write an LSP client. Use Microsoft's `multilspy` (Python) which wraps
  typescript-language-server, OmniSharp, pyright, gopls, rust-analyzer behind one API and
  downloads servers on demand. Compare Serena's `solidlsp` fork.
- Precompute a **context pack** per file before the model sees it: symbols defined here, every
  reference to them as file:line, what this file imports. Callers arrive with the file, so
  following flows is deterministic rather than hoped-for.
- Fallback when no server / restore fails: import-graph context pack (regex or tree-sitter).
- Language servers on this Mac (2026-09-10): typescript-language-server, tsserver, csharp-ls,
  roslyn-language-server (dotnet tool), plus `~/.claude/roslyn-claude-shim.py`. Missing: pyright,
  vscode-css-language-server.

### Passes
1. **Per file** with its context pack. This is the coverage guarantee.
2. **Per flow slice.** Detect entry points (Minimal API `Map*` calls, Next.js `page.tsx` routes,
   configurable globs) and analyze each entry point's transitive callee set as one unit. Entry
   points are checked off the same way.
3. **Verify.** Fresh skeptic agents try to refute each finding. LLM audits have a high
   false-positive rate; this is what makes the report trustworthy.
4. **Synthesize.** Dedupe, rank, report.

### Features, in priority order
- Incremental with dependents: a changed file re-analyzes itself plus everything that references
  its changed symbols. Makes the tool usable per PR, not just as a yearly audit.
- Lenses per repo in a small config file (`security`, `a11y` for `.tsx`, "every query must be
  tenant-scoped" for `.cs`). Lens hash in the ledger invalidates prior analyses.
- SARIF export. GitHub code scanning ingests it, so findings show as PR annotations.
- Cost estimate before running.
- One SKILL.md, three harnesses. Claude Code, Codex, and Gemini CLI all read the Agent Skills
  format (verify Gemini). The skill is just "run scan, loop next/done, or run the driver." The
  logic lives in the CLI. Install into a project the way `npx impeccable install` does.
- Resume and ctrl-C safe by construction.

### Cut from the original sketch
- Per-file scan timestamps as the freshness signal (blob SHA instead).
- Auto-installing language servers.
- Hand-rolled LSP client.

### Stack
Python 3 stdlib for the CLI (sqlite3, subprocess, json, argparse), zero installs.
`multilspy` as an optional extra for context packs.

### Build order
- **v0**: scan, ledger, next/done, headless driver, report. No LSP. Fixes coverage alone.
- **v1**: multilspy context packs, dependents, doctor.
- **v2**: flow slices, verify pass, SARIF, lenses.

## Part 1.5: GitHub Action bridge

Same CLI on the user's own runner with their own API key, on cron, ledger restored from the
Actions cache or a branch. Findings to GitHub code scanning as SARIF, coverage as a check run.
"Auto monitors and scans on a schedule" with zero hosted infrastructure. Ships in a day and
validates demand before a SaaS exists. Semgrep et al started this way.

## Part 2: hosted scheduled scans

Open-core rule: **the cloud runs the open-source CLI unchanged.** It sells hosting, scheduling,
history, and access, never a better engine. If analysis logic lives only in the hosted service,
the skill rots and people fork it.

### Shape
GitHub App (push, pull_request, installation webhooks) -> queue -> ephemeral container with the
CLI + language servers -> shallow clone at SHA -> pull repo ledger from object storage -> run
incremental pass -> publish (SARIF to code scanning, check run with coverage) -> delete clone.
Only ledger + findings persist, never source. Postgres for accounts/repos/runs/billing; object
storage for ledgers. Incremental on every push; full re-verify only when lens or model changes.

### Hard parts
- LLM cost sets the price floor (~7.5M input tokens for a ToolbagCRM-sized first pass, before
  verify). Needs a metered component or a bring-your-own-key tier.
- Language servers need a buildable checkout: C# needs the .NET SDK + restore, TS needs
  `npm ci`. Per-stack container images, graceful fallback to import-graph packs.
- Custody of other people's source and tokens: ephemeral clones, encrypted findings at rest,
  tenant isolation, SOC 2 eventually.

### Cloud-only features worth the paywall
- Trends and drift: findings over time, time to fix, "3 files changed since the last verified
  pass touch tenant scoping."
- Verified findings as the default.
- Coverage badge and audit certificate at a SHA (code due diligence for investors/acquirers).
- Shared versioned lenses (tenancy, a11y, OWASP).
- Auto-fix PRs for verified findings, later.

### Positioning
CodeRabbit, Greptile, Sourcery review diffs. Semgrep, Snyk are rule-based. Nobody sells
"every file, provably, with a coverage number and verified findings." That sentence is the pitch.

### Sequencing
OSS CLI + skill first, the Action within the same week, hosted service only after the Action has
users.

## Deferred: monetization

Tim explicitly deferred the monetization conversation on 2026-09-10 ("cart before the horse")
and asked to be reminded. Ideas already floated: open-core, metered LLM or BYO key, audit
certificate, shared lenses, auto-fix PRs. Raise it once the OSS part exists.

## Name research (2026-09-10)

Checked npm, PyPI, GitHub top hit, and `.dev` / `.sh` nameservers. Dropped as taken on both
registries: muster, census, dossier, surveyor, everyfile, comb.

| Name | Idea | npm | PyPI | Domains |
|---|---|---|---|---|
| punchlist | Every item inspected and signed off before the job is done | taken | free | `.sh` free, `.dev` taken |
| rollcall | Every file answers "present" | free | free | both taken |
| fullpass | A full pass over the codebase | free | free | both free |
| codewalk | Walk every file (CodeWalker 740 stars on GitHub) | taken | free | `.sh` free |
| toothcomb | Fine-tooth comb | free | free | `.dev` free |
| nostone | No stone unturned | free | free | unchecked |
| linewalk | Walk every line | free | free | unchecked |

Claude's pick: punchlist (exact concept, fits the trades brand family, reads well as a CLI).
Runner-up: fullpass (cleanest availability). Tim is doing his own research.

## Prior art to keep in view
multilspy (Microsoft), Serena / solidlsp (LSP-as-MCP for agents), CodeGraph (used before on
ToolbagCRM), aider repo map, SCIP indexers, GitHub linguist-generated attribute, SARIF.
