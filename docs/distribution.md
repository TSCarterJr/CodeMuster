# Distributing and installing CodeMuster

CodeMuster has three entry points: a plugin for AI marketplace discovery, a standalone skill,
and the npm CLI. All use the same CLI engine and `skill/SKILL.md` instructions. The plugin
contains no binary, MCP server, login service, or installation hook. On first use, the skill
attempts `npm i -g codemuster` only when the CLI is missing, then verifies it or explains the
manual fallback.

## Install from the repository marketplace

After the marketplace files are published to GitHub, use the commands in the
[README](../README.md#install-and-set-up). In Claude Code, the interactive equivalents are
`/plugin marketplace add TSCarterJr/CodeMuster` and `/plugin install codemuster@codemuster`.
In Codex, the plugin browser is available through `/plugins` after adding the marketplace.
Start a new session in the repository you want to audit.

For an unpublished checkout, replace the GitHub source with its absolute local path:

```sh
claude plugin marketplace add /absolute/path/to/CodeMuster
claude plugin install codemuster@codemuster
codex plugin marketplace add /absolute/path/to/CodeMuster
codex plugin add codemuster@codemuster
```

On Windows, use a path such as `E:/source/CodeMuster`. The two catalogs point to the same
`plugins/codemuster` directory. A local marketplace stays tied to that checkout; use the GitHub
source for normal distribution. Use a coding environment with terminal access to your target
repository. Installing a skill into browser-only chat does not provide local filesystem access.

If you previously installed a standalone CodeMuster skill in the same scope, remove that copy
when switching to the plugin so the agent does not show both. Keep any personal changes first.
The CLI can still be used directly after installing the plugin.

The shared skill uses `init --yes --no-skills` without `--for` through the plugin, avoiding
duplicate project skill copies. This also skips project change hooks. The plugin bundles
session/edit context hooks that direct the agent to follow `automation` in repository config.
Use `off`, `update` (default), `review`, or `review_and_fix`; changes take effect on the next
hook invocation without reinstalling. These hooks supply instructions and perform no model
calls, CLI bootstrap downloads, or ledger writes. If you want the CLI's integrated project
skills and change notifications, choose standalone setup with `init --for <agent>` in
that scope. The plugin's fix workflow uses the current
CLI's `--retry-declined`, `--include-related`, resolved verification, and `validate` behavior.

Reload the installed plugin and review its hook trust prompt when required by the host.
The current checkout's generated files are a local candidate until published and installed.

## Update

Refresh the repository marketplace and its installed plugin:

```sh
claude plugin marketplace update codemuster
claude plugin update codemuster@codemuster
codex plugin marketplace upgrade codemuster
codex plugin add codemuster@codemuster
```

For a local Codex marketplace, skip `marketplace upgrade`: it applies only to Git sources.
Regenerate the local bundle, then run `codex plugin add codemuster@codemuster` directly.
Then start a new session. Marketplace metadata must carry a new version for new content.
The CLI's npm update mechanism is independent. Standalone skill copies do not update with
the CLI: run `codemuster skill install --for <agent>` again, with the same `--global` scope
if originally installed globally.

## Standalone skill and CLI

Release artifacts include `codemuster-plugin-<version>.tar.gz` and
`codemuster-skill-<version>.tar.gz`. The plugin archive contains manifests at its root and the
skill under `skills/codemuster`; the standalone skill archive contains `codemuster/SKILL.md`
and its license. Extract the standalone folder into your agent's skills directory. If the CLI
is already available, its `skill install` command is the simpler path.

The standalone CLI remains `npm i -g codemuster`. Use `init`, `doctor`, `scan`, `estimate`,
`run`, `status`, and `report` from a terminal as described in the [user guide](usage.md).
Running AI analysis still requires an installed, authenticated coding agent.

## Maintain and release

Edit only the authoritative `skill/SKILL.md`, root `LICENSE`, and `distribution/` files.
`distribution/plugin.json` is the portable manifest and source of the release version and
marketplace presentation. The generator writes the portable manifest, compatibility manifests,
catalogs, README, and licensed skill copies:

```sh
node scripts/stage-plugin.js
node scripts/stage-plugin.js --check
npm test --prefix npm
claude plugin validate plugins/codemuster --strict
claude plugin validate . --strict
```

Commit the generated files with their sources. Before a tagged release, advance the version
in `distribution/plugin.json` and regenerate. The release workflow rejects a tag whose version
does not match the checked-in plugin. A workflow-dispatch rehearsal may stage a different
version without modifying the tracked package or publishing it:

```sh
node scripts/stage-plugin.js --version 0.2.7 --out plugin-dist
```

The release workflow attaches both archives alongside the seven npm package archives. Plugin
archives contain the skill, license, and lightweight plugin context hooks; the standalone skill
archive contains the skill and license. Platform binaries continue to
ship through the existing npm distribution. No new npm package or publisher identity is needed.

## Official directory submissions

The repository marketplace provides direct installation. It does not create a listing in
Anthropic's directory or the public directory shared by ChatGPT and Codex. Those are separate
publisher submissions and reviews. Follow the [submission packet](marketplace-submission.md)
for the listing text, reviewer scenarios, and outstanding publication requirements.

Initial local verification on 2026-09-14, before synchronizing main: version 0.2.1 was installed through both local marketplace
catalogs using isolated Claude and Codex profiles. Installed skill bytes matched the source.
Claude strict validation and the Codex Plugin Creator validator passed. All 800 .NET tests
and 22 npm tests passed with zero skips, including archive extraction and parity checks.
No GitHub push, release workflow, npm publication, or official-directory submission was
performed by this task. The fresh-machine CLI bootstrap and live reviewer scenarios have
not been exercised. That 0.2.1 package is superseded: main was subsequently fast-forwarded
to `d65558c`, and plugin metadata now prepares 0.2.7 after Tim's reported MacBook version
0.2.6. The repository and npm queries still returned published version 0.2.0 during this
sync; the MacBook installation was not inspected. Publish the matching CLI before making
the updated marketplace package available to users. No new release is claimed here.

After reconciliation, all 815 .NET and 22 npm tests passed. Both isolated agent profiles
updated to plugin 0.2.7 with skill bytes matching the synchronized source. A real CLI init
smoke confirmed that plugin mode creates repository config without duplicate skills/hooks.
Manifest, formatting, syntax, and conflict checks passed; publication remains pending.

Official references checked on 2026-09-14:

- [Claude marketplace distribution](https://code.claude.com/docs/en/plugin-marketplaces)
- [Claude plugin reference](https://code.claude.com/docs/en/plugins-reference)
- [OpenAI plugin packaging](https://developers.openai.com/plugins/build/plugins)
- [OpenAI plugin submissions](https://developers.openai.com/plugins/deploy/submission)
