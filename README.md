# CodeMuster

Audit every file and every entry point of a codebase with your coding agent, and get a coverage
number you can check.

CodeMuster is a command-line tool plus one skill. The tool maps the code (C# with Roslyn,
TypeScript with the TypeScript compiler) and splits it into units of work: one per entry point,
holding every function on that entry point's call path, plus units for the code no entry point
reaches. It tracks each unit in a local ledger. Your coding agent analyzes one unit at a time in a
fresh context, and a second pass tries to refute every finding before it reaches the report.

## Install

You need Node.js 22 or later.

```sh
npm install -g codemuster
```

npm installs a self-contained build for your platform, about 45 MB. Mapping C# also needs the
.NET SDK, and mapping TypeScript needs the repository's own `typescript` package installed.

## Set up a repository

```sh
cd your-repo
codemuster init --yes
codemuster doctor
codemuster skill install --for claude
```

- `init` writes `.codemuster/config.json`, which you commit, and adds the ledger to `.gitignore`.
- `doctor` checks that git and both mappers work in this repository, and prints the command that
  fixes each problem, such as `dotnet restore` or `npm ci`.
- `skill install` puts the skill where your agent finds it. Use `--for claude`, `codex`,
  `gemini`, or `opencode`, and add `--global` to install it for every repository.

## Run an audit

Ask your agent to audit the repository with CodeMuster. The skill takes it from there: it scans,
analyzes each unit, records the findings, verifies them, and writes the report.

You can also drive it yourself:

```sh
codemuster scan                      # map the code and plan the units
codemuster estimate                  # rough token cost of the pending units
codemuster run --agent claude -j 4   # analyze every unit headlessly, then verify each finding
codemuster status                    # coverage so far
codemuster report --out audit.md     # findings and coverage as markdown
```

`run` also accepts `--agent codex`, `gemini`, or `opencode`. After the code changes, `scan` again:
only the units whose code changed need another pass.

## Settings

`.codemuster/config.json` holds:

- `lenses`: what to look for, each with an `id`, `instructions`, and optional `globs` and
  `languages` that scope it.
- `slice_token_budget`: roughly how many tokens of code a unit shows in full before farther
  functions shrink to their signatures. The default is 24000.
- `resolution_threshold`: the share of calls, from 0 to 1, the mappers must resolve before
  `status` calls coverage complete. The default is 0.9.
- `verify`: whether each finding gets a second pass that tries to refute it. That costs about one
  more agent call per finding. The default is `true`.
- `exclude`: globs of files to leave out, on top of the built-in exclusions (generated code,
  migrations, lock files, binaries). A glob without a slash matches file names anywhere, so a
  folder needs `folder/**`. To audit one part of a large repository first, exclude the rest, then
  `scan`.

## Updates

The `codemuster` command checks npm for a newer version at most once a day. When there is one, it
downloads that version in the background, checks it against npm's integrity hash, and uses it from
the next run on. It never updates when the `CI` or `CODEMUSTER_NO_UPDATE` environment variable is
set.

## License

MIT
