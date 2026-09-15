# Setup and hook utilization review

Implementation follow-up: HOOK2/D47 adds settings-driven proactive use. The
authoritative `automation` field supports `off`, `update`, `review`, and
`review_and_fix`; plugin context hooks and the shared skill follow that choice.
See [automatic use](usage.md#automatic-use-during-coding) for the current contract.
The investigation below retains its pre-implementation observations. The existing
CLI change hook still uses a repository fingerprint; per-file markers and blocking
completion hooks were not required for this first implementation.

Reviewed 2026-09-15 against the local checkout based on `d65558c`, including its
preserved, uncommitted distribution and launch changes. Fetching `origin/main`
found no newer commits. This is an investigation and proposal, not a new product
decision or a claim that hooks have been exercised inside every agent.

## Recommendation

Tim clarified the goal: the plugin should teach the AI how to use CodeMuster and
instruct it to use the CLI proactively during normal coding, without requiring
the user to mention CodeMuster. A reminder-only integration is insufficient.
The earlier proposal below is refined by that requirement.

Use three cooperating parts:

- **Skill instructions:** explain when to use CodeMuster automatically and how to
  follow its scan, review, findings, repair, and validation workflow.
- **Hooks:** make the integration visible in agent context and signal changed
  files or pending review at useful points in the session.
- **CLI:** determine actual freshness, map affected units, and own the ledger and
  outcomes. A change notification must not claim that code has been reviewed.

A proposed standing instruction is:

> Use CodeMuster proactively during implementation, bug fixes, and refactoring in
> this repository; the user does not need to request it by name. Follow the
> CodeMuster skill. Track the files changed for the current task. Before reporting
> completion, refresh CodeMuster's state and review pending work affecting those
> files. Handle relevant findings within the user's authorized task and report
> any remaining findings or incomplete validation.

The skill's current description primarily targets whole-repository audits and
finding recovery. Broaden it to describe this routine coding use case. Both
Claude and Codex allow implicit skill selection by default, but initially expose
skill metadata rather than the full skill body. Instructions buried only in that
body cannot ensure the skill gets selected. A plugin session-start hook can add a
short instruction to load and use the skill, including after context compaction.
Existing repository instruction files can carry the same policy during explicitly
selected project setup, preserving unrelated instructions.

Official discovery references: [Claude skills](https://code.claude.com/docs/en/skills),
[Codex skills](https://developers.openai.com/codex/skills/).
Session context references: [Claude hooks](https://code.claude.com/docs/en/hooks),
[Codex hooks](https://learn.chatgpt.com/docs/hooks).

Post-edit hooks can record affected paths, but the current implementation records
one repository fingerprint rather than a per-file dirty list. A path-aware
extension must account for shell edits and affected callers, and still validate
actual content through the CLI. Hook state is a scheduling signal, not a replacement
for scan fingerprints. Deliver at most one checkpoint reminder for the same
content state, and complete promptly using an installed binary.

The proposed everyday workflow is:

1. Install the plugin once for the selected agent.
2. Initialize each repository and establish its proactive-use instructions.
3. Work normally. At a supported checkpoint, check for tracked code changes and
   remind the agent when review is due.
4. Have the agent use the skill without a separate CodeMuster request: run `scan`,
   inspect `estimate`/`status`, and review affected pending units through `run` or
   `next`/`done`. Ordinary coding should not initiate an unrelated whole-repository
   audit every time a file changes.
5. Keep repairs in the existing explicit `fix` workflow, followed by verification
   and configured validation.

The exact automatic-repair scope remains to be selected separately from proactive
CLI use. Background/headless reviews need explicit scope, model, concurrency, and
budget limits. Coalesce edits and avoid re-entering the workflow while CodeMuster
workers are running. The proactive integration described here is not implemented.
Checkpoint events and context delivery must be validated separately for each
agent; do not implement a generic stop hook that repeatedly prevents completion.

## What exists today

| Entry point | Current behavior |
|---|---|
| Marketplace plugin | Supplies the shared skill. Its instructions use `init --yes --no-skills`, creating repository config while skipping project skills and hooks. |
| `init --for claude,codex --yes` | Installs those project skills and merges change hooks into their configuration. |
| `init --yes` | Selects all three integrated agents: Claude, Codex, Gemini. |
| `init --for gemini --yes --no-hooks` | Installs the selected skill and repository config, without installing a hook. Existing hooks are not removed. |
| `init --yes --no-skills` or `--for none` | Skips both skills and hooks. `--for` cannot be combined with `--no-skills`. |
| `skill install --for opencode` | Installs the OpenCode skill. OpenCode is absent from integrated `init` selection. |
| `codemuster hook` | Records a tracked-content fingerprint in worktree Git metadata and prints `{}`. No scan, model call, review, repair, or immediate reminder. |

Implementation: [agent setup](../src/CodeMuster.Application/AgentSetup.cs),
[CLI dispatch and init](../src/CodeMuster.Cli/Program.cs),
[skill installation](../src/CodeMuster.Application/SkillInstaller.cs), and
[shared skill](../skill/SKILL.md).

The hook does not read the agent's event payload. It locates the repository from
its working directory, checks for repository config, then hashes the Git index
and unstaged binary diff. The notification is written atomically outside tracked
files and the ledger, using `git rev-parse --git-path` for its location.

After a successful scan, `status`, `report`, `fix`, and `verify` independently
compare the current fingerprint with the scan's initial snapshot. They do not
use the hook's notification marker in that comparison. Therefore, **plugin users
already get tracked-change warnings without hooks**. Before any scan, the marker
can cause the warning; after a scan, its per-tool writes add no detection benefit.
See [GitChangeTracker](../src/CodeMuster.Infrastructure/GitChangeTracker.cs).

## Gaps to address before expanding hooks

| Gap | Practical consequence | Proposed next step |
|---|---|---|
| No immediate context or reminder | A hook runs, but the agent need not notice pending review. | Define checkpoint reminders and deduplicate them by content state. |
| Hook installation is coupled to copied skills | Plugin users cannot enable hooks independently through current setup. | Separate repository setup, skill copies, and hook choices while retaining compatible defaults. |
| Repeated full Git fingerprint after edit and shell tools | Read-only shell commands also incur process and diff work. | Check at a useful checkpoint; measure large-repository overhead before claiming it is cheap. |
| Fingerprint depends on staging representation | Staging the same working-file content produces a false freshness warning. | Base reminder identity on relevant current content; test stage/unstage and excluded-file changes. |
| Hook uses the normal npm launcher | Missing binaries can download synchronously; cached launches can start the daily updater. Download timeouts exceed the installed hook deadline. | Add an installed-binary path for hooks that does not bootstrap or update. |
| Existing handler matched only by command text | Re-running init will not migrate its matcher or timeout. Opt-out flags do not remove hooks. | Add inspection, managed upgrades, and removal that preserve unrelated settings. |
| Tool coverage differs by agent | Claude PowerShell and failed tools that partially wrote files are not covered by the emitted matcher/event. | Use verified agent-specific configuration for whichever events the proposal selects. |

Launcher evidence: [launcher main and updater](../npm/lib/launcher.js). It has no
exception for `hook`: a missing selected binary, including an uncached version
pin, is downloaded before the command runs. Background update checks can be
disabled by existing environment settings, but that does not prevent bootstrap.

Untracked files are outside the current scan and notification fingerprint until
tracked. The fingerprint also includes tracked files excluded from audit coverage.
Setup and reminder text must explain these boundaries without implying a full
review of every local file.

## Agent compatibility

Context7 had no callable tools in this session. The following is based on current
official vendor documentation checked on 2026-09-15, alongside local source.

| Agent | Emitted integration | Documentation assessment |
|---|---|---|
| Claude Code | `.claude/settings.json`, `PostToolUse`, 10-second timeout | Supported schema. Current matcher lacks the documented `PowerShell` tool and does not subscribe to `PostToolUseFailure`. [Hooks](https://code.claude.com/docs/en/hooks), [PowerShell tool](https://code.claude.com/docs/en/tools-reference#powershell-tool). |
| Codex | `.codex/hooks.json`, `PostToolUse`, 10-second timeout | Current docs support this format. `exec_command` maps to `Bash`; patch operations match `apply_patch`, `Edit`, or `Write`. Project/hook trust still applies. [OpenAI hook documentation](https://learn.chatgpt.com/docs/hooks). |
| Gemini CLI | `.gemini/settings.json`, `AfterTool`, 10,000-millisecond timeout | Supported schema and tool names: `write_file`, `replace`, `run_shell_command`. [Hook reference](https://geminicli.com/docs/hooks/reference/), [tools](https://geminicli.com/docs/reference/tools/). |
| OpenCode | No integrated hook | Requires its own plugin adapter and declared version target. V1 and V2 hook APIs differ. [V1 plugins](https://opencode.ai/docs/plugins/), [V2 migration](https://opencode.ai/v2/docs/build/plugins/migrate-v1). |

Claude and Codex also document plugin-bundled `hooks/hooks.json`. Bundling hooks is
an available alternative to installing them into project settings, but it needs
repository opt-in and a way to prevent duplicate project/plugin execution.
[Claude hook locations](https://code.claude.com/docs/en/hooks#hook-locations),
[Codex plugin hooks](https://learn.chatgpt.com/docs/hooks#plugin-bundled-hooks).

## Verification and scope

- Fetched main and ran the fast-forward check: HEAD and `origin/main` both
  `d65558c15181a39c184207b9de9325664a7881ca`. Existing work and both prior stashes
  remain present; there were no incoming commits to reconcile.
- Ran selected `InitAgentTests`, `GitChangeTrackerTests`, `SkillInstallerTests`,
  `InitTests`, and skill/embedded-skill tests through `dotnet test CodeMuster.sln`.
  All **41 selected tests passed**, zero skips: 24 Application, 1 Infrastructure,
  16 CLI. Other test assemblies had no matching cases; this was not a full-suite
  run. Receipts are in ignored `TestResults/setup-hooks-review-20260915/`.
- A disposable real CLI/Git fixture initialized with `--no-skills` produced a
  freshness warning after a tracked edit, with no installed hooks and no
  `.git/codemuster/changed` marker.
- The same fixture scanned an unstaged edit, then staged that exact content.
  Working-file SHA-256 was identical before/after staging, but `status` emitted a
  freshness warning. This verifies the staging-only false warning.
- Retained fixture: `C:/Users/timot/AppData/Local/Temp/codemuster-setup-hooks-99aab6e00a574843baa0f636d7e9ddfb/`.
- Two independent reviews reconciled source behavior and current vendor docs.
  Live tool-triggered hooks, provider authentication, host trust prompts, and
  large-repository latency were not exercised by this review.

Only review/status documentation changed. D42 remains the current behavior;
checkpoint reminders, independent hook setup, and automatic review remain
proposals. No production code, agent settings, commit, push, or release changed.
