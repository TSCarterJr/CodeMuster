# Launch readiness review and remediation

## Current assessment — 2026-09-15

The candidate is ready for a controlled pilot with a matching local CLI and plugin.
Hold the public launch for the acceptance and release gates below. The earlier fixer
and release-workflow defects are fixed locally; the main uncertainty is whether real
agent sessions reliably perform the new settings-driven workflow.

Local HOOK2 validation passed **862 .NET tests and 40 npm tests, zero skips**.
The retained .NET receipts were rechecked, and generated plugin parity passes for
all 11 files at version 0.2.7. These are local Windows results. The older packaged
runtime and live adapter checks below predate HOOK2 and do not validate its plugin hooks.

REVIEW1 adds opt-in static usage assessments and UI-only browser reviews; see
[application reviews](application-reviews.md). Combined local validation passes
**1,094 .NET tests and 40 npm tests, zero skips**, with formatting, strict plugin
validation, generated/installed parity and diff checks passing. Receipts are retained
under ignored `TestResults/review1-complete-20260915/`; the final CLI receipt replaces
an earlier failure caused by a stale expected instruction string. These local Windows
results supersede the earlier HOOK2 counts, while its live acceptance gates remain.
A real two-page browser fixture and
the built CLI recorded unreadable invoice text (1.94:1), payment collection outside
the invoice task, and absent pressed-button feedback. The CLI derived findings even
with an empty submitted findings array. A wrong screenshot hash was rejected and
coverage stayed incomplete until valid evidence arrived. The fixture contains no
payment integration; its findings have not been independently verified or repaired.
This validates browser evidence ingestion, not automatic host-plugin activation or
representative application coverage. Include the five mandatory experience areas,
blocked browser states, evidence freshness, and post-repair browser verification in
the live pilot. Full accessibility or dead-code-removal guarantees are not implied.

Live checks still show GitHub's latest release, the npm launcher, and the checked
darwin-arm64 package at 0.2.0. The latest hosted test run passed at d65558c; the
candidate remains uncommitted and is absent from that run. Both recovery stashes
remain present.

| Priority | Remaining gate | Evidence required |
|---|---|---|
| 1 | Real proactive Claude/Codex acceptance | Install the matching candidate plugin in fresh isolated sessions. Make an ordinary coding request without naming CodeMuster. Exercise off, update, review, and review_and_fix; settings changes, restart/resume, dirty work preservation, and worker recursion. Confirm scoped repair and configured tests without a repeated mode-selection question. |
| 2 | Exact candidate release rehearsal | Review and commit the intended local changes, pass the hosted three-OS suite, and build/install/smoke the final packages through the no-publish workflow. Repeat upgrade and version-pin checks against those artifacts. |
| 3 | Publishing authorization and delivery | Verify all seven npm package publishers; only the launcher was authenticated previously. Complete a successful tagged release and verify every package/version plus the matching plugin artifacts. The no-publish rehearsal cannot prove npm authorization. |
| 4 | Published installation and daily use | Run the expanded P1–P8/N1–N4 cases in the submission packet against published artifacts, including missing/old CLI and failed install. Pilot a representative repository to check checkpoint frequency, useful scope, review cost, and repair/test outcomes before announcing broad daily-use readiness. |

Start with the real plugin-session cases in disposable repositories, then a scoped
pilot in a working project. Use the repository's chosen automation mode and configure
its actual test command before repair acceptance. Hook-process tests prove the helper's
output, not that an agent follows the resulting instructions.

Claude and Codex are the first plugin acceptance targets. Live Gemini/OpenCode
acceptance and runtime checks for the remaining built architectures are needed before
making equivalent support claims. Official directory approval and maximum-scale
benchmarking can follow a verified initial release; direct repository-marketplace use
does not depend on an official directory listing.

Current provider evidence: [hosted test run](https://github.com/TSCarterJr/CodeMuster/actions/runs/34848733860),
[published release](https://github.com/TSCarterJr/CodeMuster/releases/tag/v0.2.0).

## Review scope and historical evidence

This is a setup and release-readiness assessment, not a claim that every source line or every live agent behavior has been audited. Reviewed the CLI command flow, audit/verification/fix orchestration, ledger persistence, Git integration, mapper entry points, four agent adapters, skill/setup/hooks, npm launcher/updater, plugin generation, CI/release workflows, onboarding, and live GitHub/npm state. The initial review used no model calls; subsequent remediation includes live Claude/Codex fixture repairs described below. Context7 tools were unavailable in this session; adapter details were checked using installed CLI help and official documentation where needed.

## Remediation status after the requested fixes

The original findings below describe the pre-fix snapshot; their old line numbers are historical.
The next release remains on hold for authenticated npm publisher verification, hosted candidate
validation, and fresh published-plugin acceptance. Production fixes and workflow changes are
local and uncommitted. Existing distribution work and recovery stashes are preserved.

| Item | Current result |
|---|---|
| R1: scope | Fixed locally. Single and parallel repairs use the same isolated coordinator. Validation edits outside allowed tracked paths abort and remain recoverable. Real CLI/Git regressions cover jobs 1 and 2. |
| R2: commit ordering | Fixed locally. Commit precedes successful ledger recording. Rejected commits preserve edits and leave findings unfixed. Injected ledger failure after a successful commit preserves the commit and unresolved state. |
| R3: runners | Both workflow files now use hosted runners. Live fork policy requires approval from all external contributors. Workflow migration takes effect after these changes are pushed. |
| R4: release gates | Tag builds now depend on the reusable full three-OS test workflow. Installed-package smokes exercise SQLite, both mappers, orchestration and actual fixture validation. Main protection is enabled live with strict OS checks and enforced administrators. Hosted candidate run remains pending. |
| R5: compatibility | Skill requires stable 0.2.7+, one explicit update attempt for an older CLI, capability checks, and respect for version pins. All source/generated/local copies match. Compatible public CLI must precede marketplace rollout. |
| R6: publishing | Added immutable-integrity preflight and safe partial-publication resume; regression tested. npm login and step-up authentication succeeded for the launcher: its trusted publisher matches GitHub TSCarterJr/CodeMuster and release.yml with createPackage/createStagedPackage permissions. The darwin-arm64 check requests a separate EOTP challenge; all six platform publisher checks and a successful tag publication remain pending. |
| Adapter permissions | Codex shell tool disabled; OpenCode gets a named restricted agent; Gemini gets a temporary system allowlist with extensions/MCP disabled. Claude already has an allowlist. Automated contracts pass. These controls are not an OS sandbox. |
| Rollback/cache | Exact `CODEMUSTER_VERSION` pin added to launcher; caches partition by platform/architecture. Real published 0.2.0 selected despite bundled 0.2.7, then unpin returned to 0.2.7. Older launcher must be upgraded to obtain this feature. |
| Scale | Fix pack rendering now waits for a worker slot; oversized whole-file packs fail clearly before completion is recorded. No representative maximum-scale performance claim is made. |
| Yarn | Version-selected Classic/Berry commands and Berry NDJSON parser added; actual Yarn 4.9.0 output captured and tested. |
| Coordinator/upgrade | Exclusive common-Git-directory lock added for mutating commands. Actual 0.2.0 ledger reopened with completed coverage preserved in candidate. Future downgrade schema compatibility is not assumed. |
| Website | Listing/package homepage now points to the working GitHub repository. No custom-domain hosting repair is claimed. |
| Live acceptance | Claude and Codex each confirmed a real addition defect, repaired it, passed configured assertions, force-verified resolution, and passed final validation in disposable repositories. Full fresh-plugin P1–P8/N1–N4 and live Gemini/OpenCode evaluation remain pending. |

Final local checks: **831 .NET tests and 28 npm tests passed, zero skips**. Full
`dotnet format --verify-no-changes --no-restore`, `git diff --check`, generated
plugin parity, four matching skill hashes, and Claude strict plugin validation
passed. NuGet direct/transitive advisory scan reported no known vulnerabilities.
The final self-contained Windows 0.2.7 packages were packed, installed into an
isolated prefix, and passed `scripts/smoke-package.js`. A full-test attempt during
a live CLI process failed from locked build DLLs; the clean rerun after process
exit is the reported pass. No macOS/Linux candidate runtime result is implied.

Evidence retained locally under ignored `plugin-packages/readiness/` (candidate build, staged
packages and installs), and these disposable folders:

- `C:/Users/timot/AppData/Local/Temp/codemuster-live-453122fbf1ae412d8ba622f79c0bdf86/`: live Claude/Codex repositories and reports. Initial exploratory fixes lacked configured tests; the recorded acceptance reran an intentionally restored defect with assertions configured before fixing.
- `C:/Users/timot/AppData/Local/Temp/codemuster-upgrade-4ZtGHl/`: old published ledger and exact-version selection check.
- `C:/Users/timot/AppData/Local/Temp/codemuster-berry-e03e8ea8fcea47a8ad87d71eb78b4de0/`: actual Yarn audit output; a regression fixture is checked into the test source.

Do not confuse local package smoke (synthetic agent responses) with live defect repair, or live
adapter repair with full marketplace skill acceptance. The original hosted/public results below
remain older-revision evidence, not proof that this candidate is deployed.

## Initial review evidence (before fixes)

| Check | Result and limits |
|---|---|
| Local revision | HEAD d65558c15181a39c184207b9de9325664a7881ca plus existing uncommitted distribution/documentation changes. |
| `dotnet test --nologo` | PASS: 815 total, zero failures/skips; Domain 237, Application 237, Infrastructure 154, C# mapper 40, TS mapper 15, CLI 132. Windows execution only. |
| `npm test --prefix npm` | PASS: 22, zero failures/skips; launcher, staging, plugin parity and archives. |
| `node scripts/stage-plugin.js --check` | PASS: 0.2.7, nine generated files. |
| `claude plugin validate plugins/codemuster --strict` | PASS. This is manifest validation, not live skill execution. |
| NuGet direct/transitive advisory check | No known vulnerable packages reported by configured sources. This is not a source-security proof. |
| `git diff --check` | PASS before and after review documentation changes. |
| Installed skill parity | Shared source, repository Claude/Codex copies and generated plugin skill have identical SHA-256 hashes. |
| Public website | HTTPS request to codemuster.com timed out after 15 seconds from this machine; reachability was not established. This does not prove a global outage. Verify before using it as a launch/listing destination. |
| Hosted tests | Run 34848733860 succeeded at d65558c. It does not include this dirty checkout's distribution changes. |
| Published artifacts | GitHub latest release and all seven npm packages report 0.2.0. |
| Latest tagged release workflow | Run 34762927801: builds and three OS version smokes passed; npm publish failed, GitHub release job skipped. Public 0.2.0 artifacts exist nonetheless. |
| GitHub repository settings | Public; three online self-hosted Linux/X64 runners; fork approval policy first_time_contributors; no main branch protection and no repository rulesets returned. Runner host isolation was not inspected. |
| Targeted fault checks | Two confirmed failures using real CLI/Git in disposable repositories, described below. No LLM calls. |

Hosted evidence: [test run](https://github.com/TSCarterJr/CodeMuster/actions/runs/34848733860), [tagged release run](https://github.com/TSCarterJr/CodeMuster/actions/runs/34762927801), [published release](https://github.com/TSCarterJr/CodeMuster/releases/tag/v0.2.0).

## Original blockers

### R1 — High: default serial fixing does not enforce the selected file boundary

`src/CodeMuster.Application/Fix.cs:65` uses isolated workers only for parallel or explicitly related-file runs. The default serial path runs the adapter in the original checkout and calls `GitWorkspace.CommitAsync`, which stages all tracked changes using `git add --update` (`src/CodeMuster.Infrastructure/GitWorkspace.cs:14`). No equivalent of the isolated worker's allowed-path validation runs here.

Reproduced with two tracked files, a confirmed finding in a.txt, and `fix --agent fake --path a.txt`. A deterministic test command applied edits to a.txt and b.txt. The CLI exited 0, reported one fixed file, and created a commit containing both a.txt and b.txt. A formatter/test command or an agent editing an additional tracked file can trigger this; it does not require concurrent user edits.

Required outcome: enforce explicit scope for serial fixes too, including changes introduced by validation. Reject and preserve extra edits; commit only the accepted scope. Prefer reusing the existing isolated-worker/coordinator path at concurrency one. Regression must assert committed paths and preservation/rejection of extra tracked edits.

### R2 — High: serial commit failure leaves a successful fix in the ledger

`Fix.AttemptAsync` records the outcome through `done.RunAsync` at `src/CodeMuster.Application/Fix.cs:329`, before the caller commits at line 116.

Reproduced with an executable pre-commit hook returning 1. The CLI exited 1 and HEAD stayed at the baseline, but a.txt was staged and `report` displayed `fix: fixed; fake fix`. The recorded outcome therefore survives a failed commit and can exclude the finding from later repair selection. Signing failures or other commit errors have the same ordering problem.

Required outcome: never record a successful fix before required commit acceptance; preserve recoverable edits and diagnostics on failure. Also cover interruption and the reverse boundary (commit succeeds but ledger write fails), since Git and SQLite are not one transaction.

### R3 — High: public pull requests share the self-hosted release runner pool

`.github/workflows/test.yml` handles `pull_request` and schedules Linux on self-hosted/Linux/X64. `.github/workflows/release.yml` uses those same labels for build and release jobs. Live GitHub state confirms the repository is public and the runner pool is online. The fork policy only requires approval for first-time contributors.

Untrusted test/build code can execute on these hosts. No evidence was collected that they are rebuilt in clean isolated environments per job. This is an exposure finding, not evidence of compromise. GitHub explicitly advises against self-hosted runners for public repositories because pull requests can compromise their environment. [GitHub runner guidance](https://docs.github.com/en/actions/reference/security/secure-use#hardening-for-self-hosted-runners).

Required outcome: route public PR jobs to GitHub-hosted runners, or demonstrate equivalent disposable isolation with release infrastructure separated. Do not rely on read-only GITHUB_TOKEN permissions to protect the underlying host.

### R4 — High: publishing is not gated on the exact revision's full suite

The tag workflow runs npm tests and six cross-publishes, but its three-platform smoke executes only `codemuster --version`. `publish` depends on build/smoke, not the .NET test workflow. The separate test workflow runs on main pushes/PRs and can fail while a tag still publishes. Live main has no protection or ruleset enforcing successful checks.

Required outcome: require successful full validation of the immutable release revision before publishing. Include packaged-runtime fixture checks that exercise SQLite and C#/TS mapping; a version print does not load those runtime paths. Either run all advertised architectures or document which are built but not runtime-verified. Add appropriate main/tag release controls and retain version/revision-specific evidence.

### R5 — High: new skill accepts an incompatible existing CLI

`skill/SKILL.md:8` checks that `--version` runs, but does not compare the result with the features it requires. It only installs if the executable is missing. The new workflow calls setup/recovery/validation options absent from the 0.2.0 command parser. The background updater can use its update only on a later invocation, is rate-limited to daily checks, and can be disabled.

Required outcome: define and check the minimum compatible CLI version or capabilities, update through the supported path when authorized, verify again, and stop clearly if compatibility cannot be established. Publish and verify the compatible CLI before exposing the new marketplace skill. Test an existing 0.2.0 installation as well as a missing installation. Refresh guidance is also needed for standalone skill copies, which do not follow binary updates automatically.

### R6 — Release gate: npm trusted publishing has not been proven working

The latest tagged run failed on the first platform publish with npm E404 for @codemuster/darwin-arm64@0.2.0, reporting missing package or permission. This does not establish the precise authentication cause or prove the configuration is still broken today. The currently installed npm 10.9.0 cannot execute `npm trust list`, so this review could not inspect the current publisher configuration through that command.

Required outcome: verify publisher authorization for all seven packages and the release workflow identity, then retain evidence of a successful publish. A workflow-dispatch rehearsal does not exercise publish. Document recovery from partial publication: the current loop will fail on an already-published immutable version when rerun. Do not replace existing versions or assume a later manually created release proves the automation is repaired.

## Additional issues and acceptance limits

| Area | Assessment / next action |
|---|---|
| Agent permissions | D37 claims no fixer gets a shell. Claude explicitly limits tools, but Codex only chooses a sandbox (installed help describes it as the policy for model-generated shell commands); OpenCode selects the built-in build agent. OpenCode documents build as having all tools/system commands. Reconcile this contract and enforce the intended tool restrictions; a Git worktree is not an OS sandbox. Gemini's approval mode alone is not an explicit tool allowlist. No live misuse was attempted. [OpenCode agent documentation](https://opencode.ai/docs/agents/#use-build). |
| Skill and hook setup | HOOK2 (2026-09-15) adds settings-driven proactive use and plugin-bundled session/edit context hooks; plugin init still skips duplicate project skills/change hooks. Current local validation passes 862 .NET and 40 npm tests, including deterministic hook-process fixtures. Fresh-session discovery and actual tool-triggered behavior still need live acceptance evidence. Standalone OpenCode is supported by skill install but not by integrated init's agent selection. |
| Live agent acceptance | Run P1–P8 and N1–N4 in docs/marketplace-submission.md with Claude/Codex, including proactive settings-driven use, resume, missing/old CLI, denied install, report-only requests, fix, decline recovery and final validation. Fake-agent tests prove orchestration, not model compliance or authentication. Qualify Gemini/OpenCode support unless tested too. |
| npm rollback | The launcher chooses the newest cached or bundled binary. Installing an older npm version does not force a downgrade while a newer cached build remains; disabling automatic updates does not change this selection. Establish a tested version-pinning/rollback procedure before relying on emergency rollback. Cache directories are also not partitioned by architecture, relevant to native/Rosetta use on one home directory. |
| Audit scale | Parallel fix eagerly renders every queued pack before starting workers (`Fix.cs:125`). Whole-file packs are not capped by the slice budget. Large repositories/files can create memory/context pressure. Benchmark representative large repositories before scale claims; no maximum-scale benchmark was run here. |
| Dependency ecosystem | The Yarn job always invokes `yarn audit --json`; there is no Berry command selection, despite checklist language mentioning Berry parsing. Treat Berry support as incomplete until end-to-end execution is proven or advertise the narrower support. Dependency repair often needs manifest plus lockfile; the normal file-only guard can require explicit related-file recovery. |
| Ledger/concurrency | SQLite WAL, migrations, parameterized values and serialized in-process recording are good foundations. There is no demonstrated cross-process work lease; D36 already notes two run processes cannot safely share the queue. Document/enforce one coordinator per repository. Migration tests do not substitute for an old-published-ledger-to-candidate upgrade test. |
| Coverage honesty | README correctly separates completed analysis from defect detection and test success. Excluded/generated files, untracked files, low-fidelity fallback, unresolved mapping and signature-only portions must remain visible in user expectations. |
| Official directories | Repository-marketplace distribution does not require official listing. Directory approval, live reviewer evidence, publisher assets and any required privacy/terms information remain separate work. The submission packet is a draft, not proof of acceptance. |

## What is already in good shape

- CLI-owned state and one shared skill avoid duplicating orchestration across agents.
- Domain/Application separation, real SQLite/Git infrastructure tests and golden mapper fixtures provide useful coverage.
- Verification distinguishes false positives from resolved defects, recovery retains reasons, and final validation fails without a configured command.
- Parallel worktrees restrict integrated files, scoped commits preserve unrelated untracked work, and stash restoration retains a recovery reference.
- npm uses platform packages and registry-integrity verification; plugin generation checks byte parity and includes license copies.
- Documentation is explicit about local commits, no push, source coverage limits, prerequisites and plugin-versus-standalone setup.

Dimension assessment: correctness requires the serial-fix repairs; security requires runner separation and a truthful/enforced adapter boundary; performance is adequate for tested fixtures with large-repository evidence still missing; maintainability is generally good, with divergent serial/parallel fix implementations creating avoidable inconsistency.

## Remaining release sequence

1. Review and commit the local remediation and preserved distribution changes in deliberate scope. R1–R5 code fixes and adapter permission changes are implemented with regressions.
2. Run full CI and a no-publish package rehearsal on the exact candidate revision. Exercise packaged init/scan/run/verify/fix/validate with deterministic fixtures on Windows, macOS and Linux; test old-version upgrades and rollback behavior.
3. Verify npm publishing authorization/recovery, publish the compatible CLI, confirm all seven package versions, then make the matching repository marketplace available. Publication has not been performed by this remediation task.
4. Run fresh-environment live-agent acceptance against published artifacts. Announce only the verified platforms/harnesses. Submit to official directories separately if desired.

SARIF, hosted scans, Python/Go mappers, additional lenses, and frontend-to-backend graph joins are future scope, not prerequisites for a clearly described initial C#/TypeScript product.

Disposable reproduction repositories retained at `C:/Users/timot/AppData/Local/Temp/codemuster-readiness-091a8f0e07a84f60b3ab8b3d804fa12d/` (scope and commit-failure). That was the initial review state. Subsequent production/workflow fixes and live GitHub protection changes are recorded above. No real-repository commit, push, workflow dispatch, or publication has been performed.
