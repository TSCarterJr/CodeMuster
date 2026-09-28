# Changelog

User-visible changes by released version. Add upcoming changes under Unreleased;
move them to a dated version heading before publishing. Historical entries below
start with 0.2.8.

## Unreleased

- The code map now records references as well as calls. `scan` stores the declarations that
  have no body (types, fields, constants, properties, events, enum members, and in
  TypeScript/JavaScript interfaces, type aliases and plain `const`, `let` and `var`) and every
  use of a symbol or declaration in your code: calls, reads, writes, type uses, inheritance,
  implementations, attributes and decorators, and imports, each with its file, line, column and
  the method or function it sits in. Both the C# and the TypeScript/JavaScript mappers record
  them; mapping takes about 6 to 8 percent longer. The ledger moves to schema 9 the first
  time this version opens it, and CodeMuster 0.3.7 and earlier then refuse that ledger, so
  back up `.codemuster` if you may need to roll back.
- Calls through an interface are references too. Interface, abstract, extern and partial
  method definitions, interface indexers, TypeScript interface method and property signatures,
  abstract methods, get and set accessors, and overload signatures are recorded as
  declarations, so a call such as `quotes.ListQuotes(...)` through an injected
  `IQuoteService` shows up as a use of `IQuoteService.ListQuotes`. A C# `new` of a type
  without a declared constructor (a record's primary constructor, or none) is recorded as a
  call to the type.
- References now feed reviews. A changed type header, field, property, constant, enum member or
  interface member plans an `impact` unit too, and every impact unit walks up through the code
  that reads, writes or names the change as well as its callers; the pack lists each use with
  its kind and `path:line:column`. The opt-in `dead_code` review no longer reports anything the
  map records a use of, and now also reports private or internal types, fields, properties and
  constants that nothing references (public, exported, attributed and serialized ones stay
  protected). New `codemuster map references <symbol> [--kind <kind>] [--format text|json]`
  prints what the MCP `references` tool answers.
- New `codemuster mcp`: a read-only MCP server over standard input and output, so an agent can
  ask the stored map what a language server would answer. Its tools are `find_symbol`,
  `references`, `callers`, `callees`, `call_path`, `impact`, `http_links`, `entry_points` and
  `duplicates`; answers name files changed since the scan, and `--refresh` scans first when the
  map is stale. The Claude and Codex plugin registers the server, and `init` adds it to
  `.mcp.json`, `.codex/config.toml` or `.gemini/settings.json` for the agents it sets up
  (`--no-mcp` skips that). Claude Code asks you once to approve a project's `.mcp.json` server.
- Faster startup and scans: release builds are precompiled (ReadyToRun), which on Windows x64
  takes about half a second off `doctor` and about a second off `scan`, for about 28 MB more
  on disk. Only `scan` reads git history now, without rename detection, so `status`,
  `doctor`, `run`, `verify` and `fix` no longer walk it (`doctor` went from 3.0 s to 1.1 s
  on a 25,000-commit repository), and offline partial (blobless) clones can be scanned.
- When `scan` cannot read git history it prints one warning, completes, and keeps the last
  commit each file had from an earlier scan instead of clearing it.

## 0.3.7 - 2026-09-27

The ledger moves to schema 8 the first time 0.3.7 opens it, and CodeMuster 0.3.6 and earlier
then refuse that ledger, so back up `.codemuster` if you may need to roll back. Use
`npm install -g codemuster@0.3.7` once to receive the launcher as well as the native binary.

- Spend tracking: every agent call that `run`, `verify` and `fix` make is recorded with its
  input, output and cache tokens, the model that answered, and its cost, including failed calls,
  unusable answers and rejected repairs. The cost is the harness's own figure when it reports
  one (Claude Code, OpenCode), otherwise the tokens at a price table bundled with CodeMuster
  (Anthropic, OpenAI and Google models, checked 2026-09-27); it is fixed when the call is
  recorded, and a model without a price is shown as unpriced, never as free. `status` adds a
  spend line, `report` a Spend section by model, unit kind and run, and `estimate` a cost per
  model for the pending work. Figures are API-equivalent: on a subscription you pay the
  subscription. Add `prices` to `.codemuster/config.json` to price other models or use your
  own rates. `intelligent-config` prints its call's usage and cost.
- The claude adapter now asks for `--output-format json` and codex for `exec --json` to read
  usage; the answer text passed on is unchanged. A Claude Code call that ends in an error
  (`is_error`) is now a failed attempt with its message, instead of an answer to parse.

- `scan` now stores a code map in the ledger: every method and function with its file,
  lines, signature and containing type or class, every call between them, every entry
  point, and the commit scanned. Source text is not stored, and units, coverage and
  fingerprints are unchanged. The ledger moves to schema 8 the first time this version
  opens it; CodeMuster 0.3.6 and earlier then refuse that ledger and ask for an update,
  so back up `.codemuster` if you may need to roll back.
- New `codemuster map`: `map` summarizes the stored map, `map callers <symbol>` and
  `map callees <symbol>` show who calls a method and what it reaches, and
  `map flow <entry point>` shows the call tree from an endpoint, page or worker. Output is
  text, Mermaid (`--format mermaid`) or JSON, and `--out map.html` writes a self-contained
  interactive page that works offline.
- The map links React calls to the C# API: `fetch` and `axios` calls with a readable URL
  (including `${API_URL}/...` base addresses) are matched to endpoints by method and route,
  so a page's flow continues through the API to the database code. Calls that match no
  endpoint and endpoints the UI never calls are listed by `map`. Units, coverage and
  fingerprints are unchanged.
- JavaScript is mapped as well as TypeScript: `jsconfig.json` is read like `tsconfig.json`, and
  a repository with neither gets one default program over its scripts, so a JavaScript React or
  Express project gets slices and entry points instead of whole-file units only. Express, Koa and
  Fastify routes become `http` entry points (with their `app.use` prefixes), and `map` links UI
  calls to them. Mapping JavaScript needs Node.js and a `typescript` package
  (`npm i -D typescript`). Loose scripts with no config, no TypeScript files and no `typescript`
  package, such as an ASP.NET `wwwroot/js/site.js`, stay whole-file units with a note instead of
  failing the map or `doctor`.
- `doctor --fix` offers to run the setup commands `doctor` finds: `git init` and a first
  commit, `dotnet restore`, and the package manager's install where `typescript` is missing
  (`npm i -D typescript` or its pnpm or yarn form for JavaScript with no config). It asks before
  each one on a terminal, `--yes` runs them all, and redirected or CI runs only print them; it
  then checks again. Without the .NET SDK, `doctor` now says so and links to the download page
  instead of showing a raw error. It never installs the .NET SDK, Node.js or Git.
- New configurations carry a `simplify` lens that flags comments restating the code,
  commented-out code, stale comments and needless complexity. `report` lists these findings in
  their own Simplifications section, and `fix` repairs them only with `--include simplification`.
  Existing configurations are not changed, because a new lens re-audits every unit; add the lens
  yourself to use it.
- Faster scans and runs. C# documents are bound in parallel and a project listed by two
  solutions is mapped once (a scan of CodeMuster's own repository went from about 30 s to 18 s);
  TypeScript files that several `tsconfig.json` files share are parsed once; dependency audits run
  while the code is mapped, with npm, pnpm and yarn audits side by side (a fixture scan went from
  11.1 s to 7.3 s); and `run` and `verify` start the next unit as soon as a worker is free instead
  of waiting for the slowest unit of each batch. Maps, units and fingerprints are unchanged.
- Impact review: when a `scan` finds a method or function whose body or signature changed since
  the previous scan, it plans an `impact` unit. Its pack shows the old text (from Git at the
  previous scan's commit) and the new text, the callers up to 4 calls up (following UI calls into
  the API), the callees as signatures, and the endpoints and pages that reach the change, and asks
  whether anything upstream or downstream now breaks or misuses it. New `codemuster impact` lists
  those units and what each change reaches, and `impact --since <ref>` does the same for the
  symbols in files committed since a ref, without a model call. On by default; `"impact": false`
  turns it off. An impact unit waits until it is analyzed: a later scan that finds its symbol
  unchanged keeps it, and a further change keeps the version it was first planned against.
- Duplicate review: each `scan` groups methods and functions (at least 6 lines, in one language,
  outside excluded and generated files) whose bodies are the same once names and literals are
  set aside, and plans one `duplicate` unit per group of up to 12 copies. The agent decides
  whether and where to consolidate them and reports `simplification` findings, which `report`
  lists under Simplifications and `fix` repairs only with `--include simplification`. On by
  default; `"duplicates": false` turns it off.
- UI and API design review: each `scan` plans one `architecture` unit over the UI's structure (page
  routes, navigation, section headings, and form controls or settings with their labels, shown
  as a tree without code) that asks about misplaced or duplicated settings and actions,
  inconsistent names, orphan pages and buried features, and one `api` unit over every HTTP
  endpoint (method, route, handler, signature and attributes such as `[Authorize]`) that asks
  about inconsistent naming, verbs, error shapes, pagination and missing authorization. Each costs
  one agent call and reruns only when its files or their structure change. On by default;
  `"architecture_review": false` turns both off. The ledger now also stores the UI structure
  with the code map.
- A `scan` with nothing to map again is faster: when no mapped file and no project, build or
  package file changed since the last complete map, and CodeMuster's mappers are the same build,
  the scan reuses the stored map instead of loading MSBuild and the TypeScript compiler, and says
  `reused the code map from <commit>`. The result is exactly what mapping again gives. A partial
  map is never reused. `scan --remap` maps again regardless, for example after a new .NET SDK or
  a `dotnet restore` or `npm install` that changed no tracked file.

## 0.3.6 - 2026-09-25

Fixes chosen from a review of 0.3.5 (`docs/product-value-review.md`).

- A `git`, `node` or `dotnet` program committed to a repository, or sitting in the
  folder you run CodeMuster from, no longer runs in place of the real tool. Git,
  Node.js, agents, audit tools and `test_command` are found only in absolute `PATH`
  directories, and on Linux and macOS a file without the execute bit is skipped. A
  relative `PATH` entry such as `node_modules/.bin` no longer satisfies
  `test_command` (use `["npx", "jest"]`). On Windows, programs CodeMuster starts
  inherit `NoDefaultCurrentDirectoryInExePath=1`, so a script run by `test_command`
  must name a program in the current folder with a path, such as `.\build.cmd`.
- An untracked nested repository or worktree, such as a Claude Code worktree under
  `.claude/worktrees/`, no longer makes `status`, `report`, `scan`, `fix`, `verify`
  or the hook exit 1, and many untracked files no longer slow them down. The change
  warning names what changed (`web/lib/index.ts changed since the last scan`) and
  no longer fires for untracked or excluded files, including `report --out
  audit.md`, or for staging unchanged content. `codemuster hook` always exits 0, so
  it never fails an agent's tool call. `init` adds PowerShell to the Claude hook
  matcher and repairs an existing CodeMuster matcher, keeping a timeout you raised.
  A repair rewrites that settings file as plain JSON, so its comments and trailing
  commas are not kept.
- Committing content that was analyzed while modified no longer marks it stale
  under `core.autocrlf=true` (the Git for Windows default) or in a SHA-256
  repository.
- `verify` and `scan` no longer re-check unchanged findings on every cycle. Each
  re-check was a model call.
- A failed dependency audit (offline, registry or restore error) warns and keeps the
  earlier vulnerable-package findings instead of recording none. A folder with
  lockfiles for more than one package manager is audited once, with a warning that
  names the tool used, instead of making every scan exit 2. When the preferred tool
  is not installed, the next lockfile's tool is used. pnpm findings no longer call
  every vulnerable package indirect.
- `fix` works with `diff.noprefix`, `color.ui=always`, `diff.external` and similar
  Git settings, which used to discard every repair. It never records a finding
  fixed unless the file changed. It runs `test_command` once on the unmodified tree
  before calling any agent and stops when the suite already fails or the program is
  missing; `fix --allow-failing-tests` skips that check.
- With TypeScript 7, which has no JavaScript compiler API, the mapper uses
  `@typescript/typescript6` from the same project when it is installed, and
  otherwise says in one line what to install instead of printing a Node stack trace.
- Argument mistakes name the problem and show the command's usage line instead of
  the full overview, and a mistyped command gets a suggestion. `--name=value` and
  `-j4` work. `next`, `status`, `done` and `skill` reject options they do not read,
  so `next --pth web` no longer reviews the whole repository. Errors start with
  `error:` and no longer end in `(Parameter 'name')`.
- `--agent fake` is refused unless `CODEMUSTER_TEST_AGENT=1` is set. It is for
  CodeMuster's own tests, and in `fix` it commits placeholder edits.
- A .NET solution with one project that cannot restore is reported as a failed
  audit, keeping its earlier findings; restore or exclude that project.
- Scans with `exclude`, lens or `user_experience` globs no longer slow down with the
  number of files: 3,000 files and 10 globs went from about 10 s to 1.5 s. The npm
  launcher skips its update check for `codemuster hook` and inside CodeMuster
  workers.

Use `npm install -g codemuster@0.3.6` once to receive the launcher change as well as
the native binary; older launchers update only the binary.

## 0.3.5 - 2026-09-21

- A repair that cannot be integrated no longer ends the whole run. Previously one
  such file cancelled every other worker mid-call, so a parallel `fix` could pay
  for many agent calls and keep no repairs. The run now stops starting new
  repairs, lets the calls already running finish, and retains each of those
  repairs unapplied in its own worker checkout with the path reported, so they
  can still be recovered. The file that could not be integrated is reported and
  the command exits non-zero.
- The message for a file changed outside the allowed scope during validation no
  longer blames the configured test command. CodeMuster compares the checkout
  before and after validation, so it now says the change came either from the
  test command or from something else using the same checkout. Running `fix`
  while another tool or agent writes to the same checkout is what produces this.

## 0.3.4 - 2026-09-20

- `codemuster fix` no longer stops the whole run when one file is too large for a
  whole-file pack. Previously a single oversized file at the head of the queue
  ended the run with nothing repaired. That file is now recorded as skipped with
  its reason, every other file is still repaired and committed, the summary
  reports the skipped count, and the exit code stays zero. Raise
  `slice_token_budget` in `.codemuster/config.json` or split the file and the next
  `fix` picks it up, with no extra flag and no rescan.
- A file whose repair already completed is revisited when the audit later confirms
  a finding in it that the repair never answered.

## 0.3.3 - 2026-09-19

- Show the actual Codex model and thinking level before agent work by reading
  effective repository settings. Explicit flags override those settings, and all
  workers use the values shown. If lookup fails, request explicit model and effort
  instead of showing unknown defaults.
- Forward launcher-only cancellation on Unix while preserving graceful Windows
  Ctrl+C handling. Improve subprocess cleanup and retain isolated repair worktrees
  when a worker fails.
- Correct C# and TypeScript mapping for inherited actions, linked documents,
  standalone projects, operators and method-ID collisions; surface TypeScript
  configuration diagnostics.
- Strengthen configuration validation, glob matching, audit-pack code fences,
  stale dependency handling and per-target UX evidence requirements.
- Improve report layout, protect build output during package staging, and fix
  test-process and temporary-file cleanup.

Use `npm install -g codemuster@0.3.3` to receive the launcher changes as well as
the native binary.

Version 0.3.2 was tagged but not published; its release was stopped for the
Windows cancellation correction.

## 0.3.1 - 2026-09-19

- Add a compact terminal banner, highlighted provider/model/thinking settings and
  countdown bar before agent commands. Add colored audit progress counts and an
  elapsed activity indicator for intelligent configuration, with explicit results.
- Respect `NO_COLOR`, keep redirected/CI output plain, and disable animation and
  the countdown for `TERM=dumb` terminals.

## 0.3.0 - 2026-09-19

- Show the intervening release notes after `codemuster update`.
- Check for a newer version on every normal command and report the result without
  changing command output or stopping work when the registry is unavailable.
- Include this changelog in the launcher and every platform package.
- Preview the worker limit, provider, model and thinking level before `run`, `verify`
  or `fix`. Interactive terminals wait ten seconds; Enter starts immediately and
  Escape or Ctrl+C cancels. CI and redirected commands start without waiting.
- Add `intelligent-config` to inspect repository context with an AI agent and apply
  validated exclusions, scoped lenses and detected test setup. Preserve existing
  custom settings and keep an exact backup before replacing the config.

Upgrade from 0.2.9 or earlier with `npm install -g codemuster@0.3.0` to
install the new launcher. Older launchers update only the native binary, so their
`codemuster update` cannot add release-note or per-command update notices.

## 0.2.9 - 2026-09-19

- Exclude non-code documents, data, configuration and database files from AI review,
  including Markdown, text, JSON, YAML, XML, CSV, Office documents and SQLite files.
  Mapper metadata and ecosystem dependency audits retain their required inputs.
- Mark oversized packs as skipped once and continue other work. Skips retain a
  reason, consume no agent attempts or retries, and never count as reviewed.
- Show skipped work in status and reports. Rescan or use `run --force` to retry it.
- Upgrade the ledger to schema 7 while preserving audit history. Older CLIs cannot
  open this schema. Run `codemuster scan` after upgrading to refresh exclusions.

## 0.2.8 - 2026-09-15

- Add settings-driven proactive plugin workflows for Claude and Codex, with shared
  setup, skills and hooks.
- Isolate fix workers, enforce repair scope, preserve dirty work through explicit
  stash handling, and validate fixes before recording success.
- Add opt-in static usage assessments and browser-backed user-experience reviews.
- Prevent dead-code scans from failing on wall-clock regex timeouts.
- Support exact CLI version pins and separate update caches by OS and architecture.
