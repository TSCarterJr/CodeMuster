# Contributing to CodeMuster

Read [AGENTS.md](../AGENTS.md), [DECISIONS.md](../DECISIONS.md), and
[MVP-Checklist.md](../MVP-Checklist.md) before changing code. [idea.md](../idea.md) captures the
original concept; decisions and implemented behavior take precedence over early proposals.

## Build and test

Use the .NET 10 SDK, Node.js 22 or later, and Git. CI currently uses Node.js 24 and tests on
Linux, Windows, and macOS. Restore the fixture dependencies as well as the main solution:

```sh
dotnet restore
dotnet restore fixtures/mixed-repo/MixedRepo.sln
dotnet restore fixtures/minimal-api/MinimalApi.sln
npm ci --prefix fixtures/mixed-repo/web
dotnet build --no-restore
dotnet test --no-build
npm test --prefix npm
```

On a busy machine, `dotnet test -m:1` serializes project builds and test runs. Use a test filter
while iterating, then run the full suite for the completed task.

Run the development CLI without replacing your installed version:

```sh
dotnet run --project src/CodeMuster.Cli -- --help
```

To use it from a separate fixture repository after building, invoke the DLL by absolute path:

```sh
dotnet /path/to/CodeMuster/src/CodeMuster.Cli/bin/Debug/net10.0/codemuster.dll --help
```

CLI tests use `--agent fake`, temporary Git repositories, and fixture responses. They do not
spend LLM calls. The CLI accepts `--agent fake` only when `CODEMUSTER_TEST_AGENT=1` is set, because
the fake agent commits placeholder edits in `fix`; the CLI tests and `scripts/smoke-package.js` set
it. Set it yourself to try the fake agent by hand in a throwaway repository. Infrastructure tests exercise real Git and SQLite; application tests use Domain
interface fakes. Keep cross-platform paths normalized in assertions.

## Layout and boundaries

| Directory | Responsibility |
|---|---|
| `src/CodeMuster.Domain` | Models, response schemas, and I/O interfaces; no dependencies |
| `src/CodeMuster.Application` | Planning, packs, audit/fix loops, status, and reports |
| `src/CodeMuster.Infrastructure` | SQLite, Git, file system, processes, agent adapters, and dependency audits |
| `src/CodeMuster.Mapping.CSharp` | Roslyn mapper |
| `src/CodeMuster.Mapping.TypeScript` | TypeScript compiler script and Node runner |
| `src/CodeMuster.Cli` | Argument handling, help, console output, and composition |
| `npm` | Launcher, updates, platform-package staging, and launcher tests |
| `skill/SKILL.md` | Bundled instructions copied by `skill install` |
| `distribution` | Source plugin metadata, README, and context hook adapter |
| `plugins/codemuster` | Generated, self-contained marketplace plugin; do not hand-edit |
| `.claude-plugin` / `.agents/plugins` | Generated Claude/Codex marketplace catalogs |
| `tests` / `fixtures` | Test projects and small repositories with known code graphs |

Application depends on Domain. Infrastructure and mappers implement Domain interfaces. CLI
wires them together. Keep new I/O out of Application, and do not add packages outside the
project's task and dependency rules.

## Completing a change

Claim the applicable checklist task. Write and run a failing regression before changing
production behavior, implement the smallest fix, and run the full suite. Mark the task complete
with evidence and commit it separately using its task ID. Preserve unrelated local work.

For CLI changes, cover the real binary's exit code and output where practical. Keep root and npm
READMEs aligned. Update [the user guide](usage.md) and command help when an option or workflow
changes. Do not advertise planned features as available.

Maintain user-visible changes under `Unreleased` in the root `CHANGELOG.md`. Before a release,
move that section to `## X.Y.Z - YYYY-MM-DD`. Stable package staging requires a nonempty entry
for the target version and includes the complete changelog in the launcher and every platform
package. Use the same version entry for GitHub release notes. Do not edit already-published
package bytes to retrofit notes.

The release workflow builds self-contained platform packages and injects the release version.
Development builds do not constitute published releases. Local installs and published versions
use plain `0.#.#` versions; do not add a `-local` suffix. Installing, tagging, and publishing are
separate actions from building and testing a change.

After editing `skill/SKILL.md`, `LICENSE`, or `distribution/`, run
`node scripts/stage-plugin.js`. The generated plugin and catalogs are checked in so Git
marketplace installs work without a build. `npm test --prefix npm` checks their parity and
rejects stale generated files. Set the next version in `distribution/plugin.json` before a
release and regenerate; release tags must match it. See [distribution](distribution.md)
for local plugin validation, archive staging, updates, and submission preparation.


## Release gates and partial publication recovery

The tag workflow calls the full three-OS test workflow on the same revision before building.
PR, build, smoke, and release jobs use GitHub-hosted runners. Main requires the three OS test
checks. Packaged smoke tests exercise init, SQLite reopening, C#/TypeScript mapping, audit,
verification, isolated fix orchestration, and actual fixture build validation. The smoke's
finding/fix responses are synthetic; they are not live-model acceptance.

Runtime smoke targets are Windows x64, Linux x64, and macOS arm64. Windows arm64, Linux arm64,
and macOS x64 are cross-built but are not runtime-verified by this matrix. Do not advertise
those extra architectures as tested. Dispatch the release workflow with the candidate version
for a no-publish rehearsal after the source is pushed; a successful local Windows run does not
substitute for hosted execution.

`scripts/publish-packages.js` verifies package identity and registry integrity for all seven
archives before publishing any missing packages, platforms first. Identical published bytes
are skipped; a mismatch or registry error stops the run. To resume a partially published tag,
rerun failed jobs using the original retained packages. Do not rebuild and overwrite immutable
npm versions. If original bytes cannot be recovered or differ, release a new version. Confirm
all seven package versions before announcing the matching plugin. Registry preflight is not
proof of trusted-publisher authorization; the first successful tag publication must establish it.

The configured npm trusted publisher must match this repository and `release.yml` for each of
the launcher and six platform packages. Authentication to inspect that configuration is still
required; do not replace it with a long-lived token in source. Official marketplace review is
separate from repository-marketplace availability.
