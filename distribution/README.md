# CodeMuster

Audit your repository with tracked coverage, verify reported defects, and fix confirmed
findings through your coding agent. The plugin contains one skill and context hooks; the standalone CLI owns
the mapping, work queue, local ledger, and reports.

During coding, the plugin directs your agent to follow `automation` in
`.codemuster/config.json`: `off`, `update` (default), `review`, or `review_and_fix`.
It reads the setting again as you work, so changing modes does not require reinstalling.
Review and fix mode authorizes scoped CLI repairs and their local commits without another
mode-selection question. Configure `test_command` to validate those repairs.

Optional `dead_code` and `user_experience.enabled` settings start disabled. Static unused-code
candidates preserve externally callable endpoints and remain report-only. UI review runs only
against recognized/configured UI files through the active agent's available browser. It checks
the primary user flow, readability, rendering, button feedback, text quality and error recovery,
records screenshot evidence, and leaves unavailable
browser work incomplete. The plugin does not install a browser. Headless source review cannot
complete UX coverage; subjective UX recommendations never enter automatic repair.
See [application reviews](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/application-reviews.md)
for settings, the browser workflow and current scope limits.

You can also ask: **Audit this repository with CodeMuster.** To inspect existing work, ask:
**Show CodeMuster coverage and unresolved findings.** Explicit requests remain available in off mode.

On first use, the skill checks `codemuster --version`. If the command is missing, it tries
`npm i -g codemuster` once and checks again. If it cannot install or run the CLI, it explains
the error and gives you the manual installation command.

Plugin-driven setup uses `init --yes --no-skills` without `--for` to avoid installing a duplicate project
skill and project change hooks. The plugin supplies its own session/edit context hooks,
which instruct the active agent to use the CLI; hooks do not run reviews or fixes themselves.
For the CLI's integrated project skills and change notifications, use standalone setup with
`init --for <agent>` instead of a plugin in that scope.

Declined findings can be retried through `fix --retry-declined`, with an explicit
`--include-related` file scope when needed. Current-code verification records resolved
findings, and `codemuster validate` runs the configured final build/tests even with no fixes
left. The selected CLI release must support these commands; report unsupported commands
rather than silently replacing the workflow with manual edits.

You need terminal access to your repository, Node.js 22+ with npm, and Git. Install and
authenticate your coding agent separately. C# mapping needs a suitable .NET SDK; TypeScript
mapping needs the repository's TypeScript dependency. `codemuster doctor` reports missing
requirements. Installing this plugin alone does not make a browser-only chat able to access
your local repository or run the CLI.

CodeMuster stores its ledger locally under `.codemuster/`. Analysis packs contain repository
code and go to the coding agent/provider you choose. Review that provider's data controls.
The plugin adds no MCP server or separate account. Fix runs change files and create local
commits; they do not push them. Coverage measures completed analysis, not proof that code is
free of defects.

Use either this plugin or a standalone copy of the skill in the same agent scope to avoid
duplicate entries. Plugin updates come through the agent's marketplace; the CLI has its own
update mechanism. Standalone skill copies are refreshed with `codemuster skill install`.

See the [installation guide](https://github.com/TSCarterJr/CodeMuster#install-and-set-up),
[user guide](https://github.com/TSCarterJr/CodeMuster/blob/main/docs/usage.md), and
[issue tracker](https://github.com/TSCarterJr/CodeMuster/issues).

Personal and internal business use is permitted under the included LICENSE. Resale,
commercial forks, paid access/services, and external redistribution require written permission.
