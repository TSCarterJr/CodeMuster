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

## Updates

`codemuster` checks npm for a newer version at most once a day, downloads it in the background,
checks its integrity, and uses it from the next run on. It never updates when the `CI` or
`CODEMUSTER_NO_UPDATE` environment variable is set.

Website: https://codemuster.com
