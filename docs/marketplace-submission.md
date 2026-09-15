# CodeMuster marketplace submission packet

Prepared for the first plugin release. This packet is a draft, not evidence of submission,
approval, public listing, or live agent behavior. The repository marketplace can distribute
the plugin independently once its files are published.

## Listing

| Field | Proposed value |
|---|---|
| Name | CodeMuster |
| Publisher | Tim Carter |
| Category | Developer tools / code review |
| Short description | Settings-driven code reviews and tracked audits |
| Website | https://github.com/TSCarterJr/CodeMuster |
| Source | https://github.com/TSCarterJr/CodeMuster |
| Plugin directory | `plugins/codemuster` |
| Support | https://github.com/TSCarterJr/CodeMuster/issues |
| License | CodeMuster Personal and Internal Business Use License; include the root LICENSE |

Description:

> CodeMuster helps your coding agent review code during normal development according to repository
> settings, or audit an entire repository with tracked coverage. It maps code into work units,
> records analysis, verifies findings, and repairs confirmed defects when configured or requested.
> The plugin uses the CodeMuster CLI and attempts npm installation
> if it is missing. Requires a coding environment with terminal access. Coverage tracks work
> completed; it does not guarantee defect-free code.

Starter prompts:

- Audit this repository with CodeMuster.
- Show CodeMuster coverage and unresolved findings.
- Fix confirmed CodeMuster findings and run tests.

## Environment and data access

Requires Node.js 22+ with npm, Git, terminal access, and an authenticated supported coding
agent. C# mapping additionally needs the .NET SDK; TypeScript mapping needs the target
repository's TypeScript dependency. No CodeMuster login or MCP connection is required.

The skill invokes the local CLI. First use may download CodeMuster and its matching platform
package from npm. Audit packs include source code and are processed by the selected coding
agent/provider; their authentication, billing, and data controls apply. State is stored in a
local SQLite ledger. Session/edit hooks read the repository's automation setting and supply
instructions to the active agent. Fixes edit files and create local commits after an explicit
fix request or when `review_and_fix` is configured for proactive use.
CodeMuster does not push those commits. The plugin does not provide repository access in a
browser-only chat.

## Reviewer scenarios

Run against disposable copies of `fixtures/mixed-repo` or `fixtures/minimal-api`, initialized
as Git repositories. Restore their documented dependencies first. For reproducibility, set
`vulnerabilities` to `false` in the generated config when testing code findings; dependency
advisories change over time. Use the reviewer's authenticated coding agent for behavior checks.
The automated CLI fixture suite uses `--agent fake` and proves the engine loop, not that a live
model follows every skill instruction.

These are proposed fresh-plugin reviewer cases, not recorded passes. Separately, local
Claude/Codex adapters completed real defect repair with configured tests, resolved verification
and final validation; see [readiness evidence](launch-readiness.md). That does not establish
fresh marketplace skill discovery or bootstrap behavior.

| Case | Prompt / setup | Expected behavior and result |
|---|---|---|
| P1: Existing CLI | “Audit this repository with CodeMuster.” CLI already on PATH. | Check version without reinstalling; initialize if needed, scan and process units; report actual coverage and findings. |
| P2: Missing CLI | Same prompt, with a fresh environment containing npm but no CodeMuster. | Attempt `npm i -g codemuster` once; verify version; continue only if executable. |
| P3: Resume | “Continue the CodeMuster audit.” Start with an interrupted fixture audit. | Resume pending work using the ledger; preserve prior outcomes and report remaining units. |
| P4: Report | “Show coverage and unresolved findings.” Use an audited fixture. | Read status and report including refuted outcomes; explain findings and verification reasons without changing source. |
| P5: Authorized fix | “Fix confirmed findings and run the fixture tests.” Configure the fixture's actual test command first. | Review findings, run the managed fix workflow, inspect diffs/tests, and explain unresolved outcomes; local commits only. |
| P6: Proactive update | “Implement this small feature.” Set automation to update; do not mention CodeMuster. | Refresh tracked coverage at the coding checkpoint without starting review or repair. |
| P7: Proactive review | Same feature request with automation review. | Review affected units through scoped CLI packs, respect verify settings, and report findings without repair. |
| P8: Proactive repair | Same feature request with automation review_and_fix and configured tests. | Preserve unrelated work, review affected units, verify candidates, repair through the CLI and validate, without asking for mode selection again. |
| N4: Mode disabled or reduced | Set off, or change review_and_fix to update during the task. | Off produces no automatic CodeMuster actions; update refreshes without proceeding to repairs. Explicit user requests still take precedence. |
| N1: Install denied | Audit prompt; environment blocks global npm installation. | Stop after the blocked/failed attempt, explain the actual reason, and tell the user to run `npm i -g codemuster`. |
| N2: Broken existing CLI | Audit prompt; existing CLI returns an error other than command-not-found. | Report the error without reinstalling or claiming an audit ran. |
| N3: Report-only declines | “Explain the declined fixes; do not edit.” Fixture has recorded declines. | Explain reasons and a proposed repair without editing files, creating commits, or clearing ledger state. |

## Submission steps and remaining requirements

1. Release and verify the repository marketplace and matching npm CLI. Test the install commands
   from a fresh environment against the published revision, not only the local checkout.
2. Run and retain the live reviewer cases above, including CLI bootstrap failure. Attach actual
   output and artifact versions. Do not submit this table as evidence of passed evaluations.
3. For Anthropic, use the official plugin submission route linked from
   [Create plugins](https://code.claude.com/docs/en/plugins#submit-your-plugin-to-the-community-marketplace).
   Supply the repository URL and plugin path, and confirm the custom license permits the
   directory's required hosting/distribution under the publisher's authorization.
4. For OpenAI, create a skills-only submission using the
   [plugin submission flow](https://developers.openai.com/plugins/deploy/submission).
   Upload the tested skill bundle, listing text, prompts, and the required positive/negative
   reviewer scenarios. Prepare the upload format requested by the current portal from the
   staged directory; downloadable release archives use tar.gz.
5. Complete publisher identity, required artwork/screenshots, public privacy/terms URLs,
   availability selections, and policy attestations using publisher-approved information.
   Those fields are not invented or asserted complete by this packet. The code/license URLs
   are not substitutes for a privacy notice if the marketplace requires one.
6. Submit for review and publish only after the platform approves it. Record the listing URL,
   accepted version, and actual release date in this document and AGENTS.md.

Official submission and directory review are external steps. Adding a repository catalog,
installing a local plugin, or passing manifest validation does not complete them.
