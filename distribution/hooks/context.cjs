'use strict';

const fs = require('node:fs');
const path = require('node:path');

const limit = 1024 * 1024;
const tools = new Set(['Edit', 'Write', 'apply_patch', 'Bash', 'PowerShell', 'NotebookEdit']);
const modes = {
  update: 'Update coverage with codemuster scan at meaningful coding checkpoints. This mode does not authorize automatic review or repair.',
  review: 'Update coverage, review the code relevant to the current task, verify findings when configured, and report results through the CodeMuster skill. This mode does not authorize automatic repair.',
  review_and_fix: 'Update coverage, review relevant code, and verify candidate findings without changing the repository verify setting. Repair confirmed findings through codemuster fix; then verify outcomes and run codemuster validate. The configured mode authorizes this scoped workflow without repeated per-run consent.',
};

function repositoryRoot(directory) {
  let current = fs.realpathSync(directory);
  if (!fs.statSync(current).isDirectory()) return null;
  while (true) {
    const marker = fs.statSync(path.join(current, '.git'), { throwIfNoEntry: false });
    if (marker?.isFile() || marker?.isDirectory()) return current;
    const parent = path.dirname(current);
    if (parent === current) return null;
    current = parent;
  }
}

function within(root, target) {
  const relative = path.relative(root, target);
  return relative !== '..' && !relative.startsWith('..' + path.sep) && !path.isAbsolute(relative);
}

function readConfig(root) {
  const file = path.join(root, '.codemuster/config.json');
  if (!fs.existsSync(file)) return null;
  const resolved = fs.realpathSync(file);
  if (!within(root, resolved) || !fs.statSync(resolved).isFile()) throw new Error('invalid config path');
  const descriptor = fs.openSync(resolved, 'r');
  try {
    const bytes = Buffer.alloc(limit + 1);
    const count = fs.readSync(descriptor, bytes, 0, bytes.length, 0);
    if (count > limit) throw new Error('config too large');
    const config = JSON.parse(bytes.subarray(0, count).toString('utf8').replace(/^\uFEFF/, ''));
    if (!config || typeof config !== 'object' || Array.isArray(config)) throw new Error('invalid config');
    return config;
  } finally {
    fs.closeSync(descriptor);
  }
}

function context(event) {
  if (process.env.CODEMUSTER_WORKER !== undefined) return {};
  if (!event || typeof event !== 'object' || Array.isArray(event)) return {};
  const name = event.hook_event_name;
  if (name !== 'SessionStart' && name !== 'PostToolUse') return {};
  if (name === 'PostToolUse' && !tools.has(event.tool_name)) return {};
  if (typeof event.cwd !== 'string' || !path.isAbsolute(event.cwd)) return {};
  let root;
  try {
    root = repositoryRoot(process.cwd());
    const eventRoot = repositoryRoot(event.cwd);
    if (!root || !eventRoot) return {};
    const rootInfo = fs.statSync(root, { bigint: true });
    const eventInfo = fs.statSync(eventRoot, { bigint: true });
    if (rootInfo.dev !== eventInfo.dev || rootInfo.ino !== eventInfo.ino) return {};
  } catch {
    return {};
  }

  let config;
  let mode;
  try {
    config = readConfig(root);
    mode = config && Object.hasOwn(config, 'automation') ? config.automation : 'update';
    if (typeof mode !== 'string' || (mode !== 'off' && !Object.hasOwn(modes, mode))) throw new Error('invalid automation');
  } catch {
    return { systemMessage: 'CodeMuster: check .codemuster/config.json. Use a valid JSON object and set automation to off, update, review, review_and_fix. Automatic guidance is paused until the configuration is valid.' };
  }
  if (mode === 'off') return {};

  const lines = [
    `CodeMuster automation=${mode}, read from this repository's .codemuster/config.json (update is the default).`,
    'During normal coding, load the CodeMuster skill and follow its CLI bootstrap and routine workflow without waiting for a separate CodeMuster request. Explicit user instructions take precedence.',
  ];
  if (!config) {
    lines.push('Repository setup is missing. Follow the skill bootstrap if the CLI is missing, then use codemuster init --yes --no-skills for plugin setup. The default automation mode is update; preserve any existing choice and do not enable a stronger mode automatically.');
  }
  lines.push(modes[mode]);
  if (name === 'PostToolUse') {
    lines.push('A coding tool completed. Check whether relevant files changed; apply this workflow at the next meaningful checkpoint and before completion, not after every tool call.');
  }
  lines.push('Keep work scoped to the current task, preserve unrelated changes, and use the CLI ledger and content fingerprints as the source of truth.');
  return { hookSpecificOutput: { hookEventName: name, additionalContext: lines.join('\n') } };
}

async function main() {
  const chunks = [];
  let size = 0;
  for await (const chunk of process.stdin) {
    size += chunk.length;
    if (size > limit) return {};
    chunks.push(chunk);
  }
  let event;
  try {
    event = JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    return {};
  }
  return context(event);
}

main().then(result => process.stdout.write(JSON.stringify(result) + '\n'), () => {
  process.stdout.write(JSON.stringify({ systemMessage: 'CodeMuster could not read hook input. Automatic guidance was not applied.' }) + '\n');
});
