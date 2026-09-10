# codemuster

Full-coverage codebase audits with a real coverage number. The CLI iterates every entry point
and every file, calls your coding agent (Claude Code, Codex, Gemini CLI, OpenCode) once per unit
of work in a fresh context, checks it off in a local ledger, and verifies findings before
reporting them.

Version 0.0.1 is a placeholder that reserves this package name; it installs nothing. The first
real release will install the platform binary so that `npx codemuster` works wherever your
coding agent is installed.

Website: https://codemuster.com
Source and progress: https://github.com/TSCarterJr/CodeMuster
