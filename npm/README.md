# codemuster

Audit every file and every entry point of a codebase with your coding agent (Claude Code, Codex,
Gemini CLI, or OpenCode), and get a coverage number you can check. CodeMuster maps the code,
hands your agent one unit of work at a time in a fresh context, tracks every unit in a local
ledger, and tries to refute each finding before it reaches the report.

## Install

You need Node.js 22 or later.

```sh
npm install -g codemuster
```

npm installs a self-contained build for your platform, about 45 MB. Mapping C# also needs the
.NET SDK, and mapping TypeScript needs the repository's own `typescript` package installed.

## Get started

```sh
cd your-repo
codemuster init --yes
codemuster doctor
codemuster skill install --for claude
```

Then ask your agent to audit the repository with CodeMuster. `codemuster` with no arguments lists
every command.

## Fixing confirmed findings

Run `codemuster fix --agent codex` to fix confirmed findings, one file at a time, with a local
commit for each file changed. Add `-j 4` to run up to four file workers concurrently (the default
is one). Each parallel worker edits an isolated Git worktree; changes outside its assigned file
are rejected. The coordinator applies finished files, runs the configured tests against the
accumulated changes, commits each file, and records results sequentially. Each available slot
takes the next file immediately. Workers need disk space for their tracked-file checkouts.

If tracked files have local edits, the terminal offers to stash
them and restore them afterward, including their staging state. Answer `y` to continue; Enter
or `n` leaves your work alone. For noninteractive use, pass `--stash` explicitly.

Untracked files stay in place and outside fix commits. The recovery stash is retained after
restoration. If restoring conflicts with a fix, CodeMuster stops, keeps the commits and stash,
and prints the backup identifier and recovery command. Interrupted fix edits are saved in a
separate stash before restoring your work. Interrupted parallel workers retain their worktrees
at the paths printed by the CLI; completed fix commits remain in place. Avoid editing the same
checkout during a fix run.

Set `"test_command"` in `.codemuster/config.json` to a program and arguments, such as
`["dotnet", "test"]`, to validate each fix before it is recorded and committed. Without this
setting, CodeMuster warns that it is not running tests.

## Updates

`codemuster` checks npm for a newer version at most once a day, downloads it in the background,
checks its integrity, and uses it from the next run on. It never updates when the `CI` or
`CODEMUSTER_NO_UPDATE` environment variable is set.

Website: https://codemuster.com
