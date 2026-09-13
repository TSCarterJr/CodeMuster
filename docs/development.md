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
node --test npm/test/launcher.test.js
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
spend LLM calls. Infrastructure tests exercise real Git and SQLite; application tests use Domain
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

The release workflow builds self-contained platform packages and injects the release version.
Development builds do not constitute published releases. Local installs and published versions
use plain `0.#.#` versions; do not add a `-local` suffix. Installing, tagging, and publishing are
separate actions from building and testing a change.
