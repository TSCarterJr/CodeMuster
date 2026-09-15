'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const repo = path.resolve(__dirname, '../..');
const script = path.join(repo, 'scripts/stage-plugin.js');

function temporaryDirectory(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'codemuster-plugin-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  return directory;
}

function stage(...args) {
  return spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' });
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

test('plugin staging ships a self-contained skill for both marketplaces with one release version', (t) => {
  const output = temporaryDirectory(t);
  const result = stage('--out', output, '--version', '0.2.1-test.1');
  assert.equal(result.status, 0, result.stderr);
  const plugin = path.join(output, 'plugins/codemuster');
  const skill = path.join(plugin, 'skills/codemuster/SKILL.md');
  assert.deepEqual(fs.readFileSync(skill), fs.readFileSync(path.join(repo, 'skill/SKILL.md')));
  for (const relative of ['LICENSE', 'skills/codemuster/LICENSE']) {
    assert.deepEqual(fs.readFileSync(path.join(plugin, relative)), fs.readFileSync(path.join(repo, 'LICENSE')));
  }
  const portable = readJson(path.join(plugin, 'plugin.json'));
  const codex = readJson(path.join(plugin, '.codex-plugin/plugin.json'));
  const claude = readJson(path.join(plugin, '.claude-plugin/plugin.json'));
  for (const manifest of [portable, codex, claude]) {
    assert.equal(manifest.name, 'codemuster');
    assert.equal(manifest.version, '0.2.1-test.1');
    assert.equal(manifest.license, 'SEE LICENSE IN LICENSE');
    assert.equal(manifest.author.name, 'Tim Carter');
    assert.equal(manifest.mcpServers, undefined);
    assert.equal(manifest.hooks, undefined);
  }
  assert.deepEqual(codex.interface, portable.extensions['com.openai'].interface);
  assert.equal(codex.skills, './skills/');
  const claudeMarket = readJson(path.join(output, '.claude-plugin/marketplace.json'));
  const codexMarket = readJson(path.join(output, '.agents/plugins/marketplace.json'));
  for (const market of [claudeMarket, codexMarket]) {
    assert.equal(market.name, 'codemuster');
    assert.equal(market.plugins.length, 1);
    assert.equal(market.plugins[0].name, 'codemuster');
  }
  assert.equal(claudeMarket.plugins[0].source, './plugins/codemuster');
  assert.equal(codexMarket.plugins[0].source.path, './plugins/codemuster');
  assert.deepEqual(codexMarket.plugins[0].policy, { installation: 'AVAILABLE', authentication: 'ON_INSTALL' });
  assert.match(fs.readFileSync(path.join(plugin, 'README.md'), 'utf8'), /npm i -g codemuster/);
  assert.equal(fs.existsSync(path.join(plugin, 'bin')), false);
  for (const relative of ['hooks.json', 'context.cjs']) {
    assert.deepEqual(fs.readFileSync(path.join(plugin, 'hooks', relative)), fs.readFileSync(path.join(repo, 'distribution/hooks', relative)));
  }
  const hooks = readJson(path.join(plugin, 'hooks/hooks.json')).hooks;
  assert.deepEqual(Object.keys(hooks).sort(), ['PostToolUse', 'SessionStart']);
  assert.match(hooks.PostToolUse[0].matcher, /PowerShell/);
  for (const event of Object.values(hooks)) {
    assert.equal(event[0].hooks[0].command, `node -e "require(require('node:path').join(process.env.CLAUDE_PLUGIN_ROOT,'hooks','context.cjs'))"`);
    assert.equal(event[0].hooks[0].async, undefined);
  }
});

test('check detects stale or missing generated files without changing them', (t) => {
  const output = temporaryDirectory(t);
  assert.equal(stage('--out', output).status, 0);
  assert.equal(stage('--out', output, '--check').status, 0);
  const skill = path.join(output, 'plugins/codemuster/skills/codemuster/SKILL.md');
  fs.writeFileSync(skill, 'stale instructions');
  const manifest = path.join(output, 'plugins/codemuster/.claude-plugin/plugin.json');
  fs.unlinkSync(manifest);
  const hook = path.join(output, 'plugins/codemuster/hooks/context.cjs');
  fs.writeFileSync(hook, 'stale hook');
  const result = stage('--out', output, '--check');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /skills\/codemuster\/SKILL.md/);
  assert.match(result.stderr, /\.claude-plugin\/plugin.json/);
  assert.match(result.stderr, /hooks\/context.cjs/);
  assert.equal(fs.readFileSync(skill, 'utf8'), 'stale instructions');
  assert.equal(fs.existsSync(manifest), false);
  assert.equal(fs.readFileSync(hook, 'utf8'), 'stale hook');
});

test('staging is repeatable and preserves unrelated files', (t) => {
  const output = temporaryDirectory(t);
  fs.writeFileSync(path.join(output, 'keep.txt'), 'user work');
  assert.equal(stage('--out', output).status, 0);
  const manifest = path.join(output, 'plugins/codemuster/plugin.json');
  const before = fs.readFileSync(manifest);
  assert.equal(stage('--out', output).status, 0);
  assert.deepEqual(fs.readFileSync(manifest), before);
  assert.equal(fs.readFileSync(path.join(output, 'keep.txt'), 'utf8'), 'user work');
});

test('invalid arguments fail before generating files', (t) => {
  const output = temporaryDirectory(t);
  for (const args of [['--version', '../bad'], ['--version'], ['--out'], ['--unknown']]) {
    const result = stage('--out', output, ...args);
    assert.equal(result.status, 2, result.stderr);
  }
  assert.deepEqual(fs.readdirSync(output), []);
});

test('checked-in marketplace bundles match the authoritative skill and metadata', () => {
  const result = stage('--check');
  assert.equal(result.status, 0, result.stderr);
});

test('release archives preserve the standalone skill and complete plugin layouts', (t) => {
  const output = temporaryDirectory(t);
  const staged = path.join(output, 'staged');
  assert.equal(stage('--out', staged).status, 0);
  const plugin = path.join(staged, 'plugins/codemuster');
  const { tar } = require('../lib/launcher');
  for (const kind of ['plugin', 'skill']) {
    const archive = path.join(output, `${kind}.tar.gz`);
    const extracted = path.join(output, kind);
    fs.mkdirSync(extracted);
    tar(['-czf', archive, '-C', kind === 'plugin' ? plugin : path.join(plugin, 'skills'), kind === 'plugin' ? '.' : 'codemuster']);
    tar(['-xzf', archive, '-C', extracted]);
    const skillRoot = path.join(extracted, kind === 'plugin' ? 'skills/codemuster' : 'codemuster');
    assert.deepEqual(fs.readFileSync(path.join(skillRoot, 'SKILL.md')), fs.readFileSync(path.join(repo, 'skill/SKILL.md')));
    assert.deepEqual(fs.readFileSync(path.join(skillRoot, 'LICENSE')), fs.readFileSync(path.join(repo, 'LICENSE')));
    if (kind === 'plugin') {
      assert.equal(readJson(path.join(extracted, '.claude-plugin/plugin.json')).name, 'codemuster');
      assert.equal(readJson(path.join(extracted, '.codex-plugin/plugin.json')).name, 'codemuster');
      assert.equal(readJson(path.join(extracted, 'plugin.json')).name, 'codemuster');
      for (const relative of ['hooks.json', 'context.cjs']) {
        assert.deepEqual(fs.readFileSync(path.join(extracted, 'hooks', relative)), fs.readFileSync(path.join(repo, 'distribution/hooks', relative)));
      }
    } else {
      assert.deepEqual(fs.readdirSync(extracted), ['codemuster']);
      assert.deepEqual(fs.readdirSync(skillRoot).sort(), ['LICENSE', 'SKILL.md']);
    }
  }
});

const contextScript = path.join(repo, 'distribution/hooks/context.cjs');

function hookRepository(t, config = {}) {
  const directory = temporaryDirectory(t);
  fs.mkdirSync(path.join(directory, '.git'));
  if (config !== undefined) {
    fs.mkdirSync(path.join(directory, '.codemuster'));
    fs.writeFileSync(path.join(directory, '.codemuster/config.json'), JSON.stringify(config));
  }
  return directory;
}

function contextHook(cwd, payload = {}, options = {}) {
  const result = spawnSync(process.execPath, [contextScript], {
    cwd,
    input: JSON.stringify({ cwd, hook_event_name: 'SessionStart', ...payload }),
    encoding: 'utf8',
    ...options,
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stderr, '');
  return result.stdout ? JSON.parse(result.stdout) : {};
}

test('context hooks teach the configured routine mode and reread it on every event', (t) => {
  const cwd = hookRepository(t);
  for (const [mode, expected] of [
    ['update', /update.*coverage/is],
    ['review', /review.*findings/is],
    ['review_and_fix', /repair confirmed findings/i],
  ]) {
    fs.writeFileSync(path.join(cwd, '.codemuster/config.json'), JSON.stringify({ automation: mode }));
    for (const hook_event_name of ['SessionStart', 'PostToolUse']) {
      const result = contextHook(cwd, { hook_event_name, tool_name: 'PowerShell' });
      assert.equal(result.hookSpecificOutput.hookEventName, hook_event_name);
      assert.match(result.hookSpecificOutput.additionalContext, new RegExp(`automation=${mode}`));
      assert.match(result.hookSpecificOutput.additionalContext, expected);
      assert.match(result.hookSpecificOutput.additionalContext, /CodeMuster skill/);
      assert.match(result.hookSpecificOutput.additionalContext, /without.*request/i);
      if (mode === 'review') assert.match(result.hookSpecificOutput.additionalContext, /verify findings when configured/i);
      if (mode === 'review_and_fix') assert.match(result.hookSpecificOutput.additionalContext, /verify candidate findings.*without changing.*verify setting/i);
      assert.equal(result.decision, undefined);
    }
  }
  fs.writeFileSync(path.join(cwd, '.codemuster/config.json'), '{"automation":"off"}');
  assert.deepEqual(contextHook(cwd), {});
  assert.deepEqual(contextHook(cwd, { hook_event_name: 'PostToolUse', tool_name: 'Edit' }), {});
});

test('missing automation defaults to update and a missing CLI does not prevent bootstrap context', (t) => {
  const cwd = hookRepository(t);
  const result = contextHook(cwd, {}, { env: { ...process.env, PATH: '' } });
  assert.match(result.hookSpecificOutput.additionalContext, /automation=update/);
  assert.match(result.hookSpecificOutput.additionalContext, /bootstrap/i);
  assert.doesNotMatch(result.hookSpecificOutput.additionalContext, /repair confirmed findings/i);
});

test('missing repository config explains init and the default without silently authorizing fixes', (t) => {
  const cwd = hookRepository(t);
  fs.rmSync(path.join(cwd, '.codemuster'), { recursive: true });
  const result = contextHook(cwd);
  assert.match(result.hookSpecificOutput.additionalContext, /init --yes --no-skills/);
  assert.match(result.hookSpecificOutput.additionalContext, /update/);
  assert.match(result.hookSpecificOutput.additionalContext, /preserve/i);
  assert.doesNotMatch(result.hookSpecificOutput.additionalContext, /repair confirmed findings/i);
  assert.equal(fs.existsSync(path.join(cwd, '.codemuster')), false);
});

test('repository root settings govern subdirectories and nested config cannot shadow them', (t) => {
  const root = hookRepository(t, { automation: 'review' });
  const child = path.join(root, 'src');
  fs.mkdirSync(path.join(child, '.codemuster'), { recursive: true });
  fs.writeFileSync(path.join(child, '.codemuster/config.json'), '{"automation":"review_and_fix"}');
  const result = contextHook(child, { cwd: root });
  assert.match(result.hookSpecificOutput.additionalContext, /automation=review\b/);
  assert.doesNotMatch(result.hookSpecificOutput.additionalContext, /repair confirmed findings/i);
});

test('repository identity follows platform path casing semantics', (t) => {
  const directory = temporaryDirectory(t);
  const cwd = path.join(directory, 'MixedCaseRepo');
  fs.mkdirSync(path.join(cwd, '.git'), { recursive: true });
  fs.mkdirSync(path.join(cwd, '.codemuster'));
  fs.writeFileSync(path.join(cwd, '.codemuster/config.json'), '{"automation":"review"}');
  const other = path.join(directory, 'mixedcaserepo');
  fs.mkdirSync(path.join(other, '.git'), { recursive: true });
  const originalInfo = fs.statSync(cwd, { bigint: true });
  const otherInfo = fs.statSync(other, { bigint: true });
  const sameDirectory = originalInfo.dev === otherInfo.dev && originalInfo.ino === otherInfo.ino;
  const result = contextHook(cwd, { cwd: other });
  if (!sameDirectory) {
    assert.deepEqual(result, {});
    return;
  }
  assert.match(result.hookSpecificOutput.additionalContext, /automation=review\b/);

  const strictComparison = spawnSync(process.execPath, ['-e', `
    const path = require('node:path');
    const relative = path.relative;
    path.relative = (from, to) => from !== to && from.toLowerCase() === to.toLowerCase()
      ? '../case-variant' : relative(from, to);
    require(process.argv[1]);
  `, contextScript], {
    cwd,
    input: JSON.stringify({ cwd: other, hook_event_name: 'SessionStart' }),
    encoding: 'utf8',
  });
  assert.equal(strictComparison.status, 0, strictComparison.stderr);
  assert.match(JSON.parse(strictComparison.stdout).hookSpecificOutput.additionalContext, /automation=review\b/);
});

test('hooks ignore unrelated cwd, nonrepositories, and unsupported or malformed events', (t) => {
  const cwd = hookRepository(t, { automation: 'review_and_fix' });
  const unrelated = hookRepository(t);
  assert.deepEqual(contextHook(cwd, { cwd: unrelated }), {});
  assert.deepEqual(contextHook(temporaryDirectory(t)), {});
  for (const payload of [
    { cwd: '.' }, { cwd: null }, { hook_event_name: 'Stop' }, { hook_event_name: null },
    { hook_event_name: 'PostToolUse', tool_name: 'Read' },
  ]) assert.deepEqual(contextHook(cwd, payload), {});
  for (const input of ['', '{', 'null', '[]', 'x'.repeat(1024 * 1024 + 1)]) {
    assert.deepEqual(contextHook(cwd, {}, { input }), {});
  }
});

test('linked worktrees use their own config and do not inherit a parent repository mode', (t) => {
  const parent = hookRepository(t, { automation: 'review_and_fix' });
  const child = path.join(parent, 'worker');
  fs.mkdirSync(child);
  fs.writeFileSync(path.join(child, '.git'), 'gitdir: ../.git/worktrees/worker');
  const result = contextHook(child);
  assert.match(result.hookSpecificOutput.additionalContext, /init --yes --no-skills/);
  assert.doesNotMatch(result.hookSpecificOutput.additionalContext, /repair confirmed findings/i);
});

test('invalid repository automation produces actionable diagnostics without action context', (t) => {
  const cwd = hookRepository(t);
  for (const value of ['bad', null, 1, false, {}, []]) {
    fs.writeFileSync(path.join(cwd, '.codemuster/config.json'), JSON.stringify({ automation: value }));
    const result = contextHook(cwd);
    assert.match(result.systemMessage, /\.codemuster\/config\.json/);
    assert.match(result.systemMessage, /off, update, review, review_and_fix/);
    assert.equal(result.hookSpecificOutput, undefined);
  }
  for (const value of ['{', '[]', 'null', '"review"']) {
    fs.writeFileSync(path.join(cwd, '.codemuster/config.json'), value);
    const result = contextHook(cwd);
    assert.match(result.systemMessage, /\.codemuster\/config\.json/);
    assert.equal(result.hookSpecificOutput, undefined);
  }
});

test('hooks do not consume injected tool content or modify repository state', (t) => {
  const cwd = hookRepository(t, { automation: 'review_and_fix' });
  fs.writeFileSync(path.join(cwd, '.codemuster/ledger.db'), 'ledger bytes');
  fs.writeFileSync(path.join(cwd, 'source.js'), 'original source');
  const before = fs.readdirSync(cwd, { recursive: true }).sort().map(file => [file, fs.statSync(path.join(cwd, file)).isFile() ? fs.readFileSync(path.join(cwd, file), 'hex') : null]);
  const result = contextHook(cwd, {
    hook_event_name: 'PostToolUse', tool_name: 'Bash',
    tool_input: { command: 'INJECTED_COMMAND', file_path: '../outside' },
    tool_response: { content: 'INJECTED_RESPONSE' },
  });
  assert.doesNotMatch(JSON.stringify(result), /INJECTED/);
  const after = fs.readdirSync(cwd, { recursive: true }).sort().map(file => [file, fs.statSync(path.join(cwd, file)).isFile() ? fs.readFileSync(path.join(cwd, file), 'hex') : null]);
  assert.deepEqual(after, before);
});

test('CodeMuster-owned workers suppress all context regardless of automation mode', (t) => {
  const cwd = hookRepository(t, { automation: 'review_and_fix' });
  for (const CODEMUSTER_WORKER of ['', '1']) {
    assert.deepEqual(contextHook(cwd, {}, { env: { ...process.env, CODEMUSTER_WORKER } }), {});
  }
});

test('configuration redirected outside the repository is rejected without revealing its contents', (t) => {
  const cwd = hookRepository(t);
  const outside = temporaryDirectory(t);
  fs.writeFileSync(path.join(outside, 'config.json'), '{"automation":"review_and_fix","secret":"DO_NOT_PRINT"}');
  fs.rmSync(path.join(cwd, '.codemuster'), { recursive: true });
  fs.symlinkSync(outside, path.join(cwd, '.codemuster'), process.platform === 'win32' ? 'junction' : 'dir');
  const result = contextHook(cwd);
  assert.match(result.systemMessage, /\.codemuster\/config\.json/);
  assert.equal(result.hookSpecificOutput, undefined);
  assert.doesNotMatch(JSON.stringify(result), /DO_NOT_PRINT/);
});

test('generated hook command runs from plugin paths containing spaces and shell metacharacters', (t) => {
  const output = path.join(temporaryDirectory(t), "plugin 'quotes' $value `tick`");
  assert.equal(stage('--out', output).status, 0);
  const plugin = path.join(output, 'plugins/codemuster');
  const command = readJson(path.join(plugin, 'hooks/hooks.json')).hooks.SessionStart[0].hooks[0].command;
  const cwd = hookRepository(t, { automation: 'review' });
  const result = spawnSync(command, {
    shell: true,
    cwd,
    env: { ...process.env, CLAUDE_PLUGIN_ROOT: plugin },
    input: JSON.stringify({ cwd, hook_event_name: 'SessionStart' }),
    encoding: 'utf8',
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(JSON.parse(result.stdout).hookSpecificOutput.additionalContext, /automation=review\b/);
  if (process.platform === 'win32') {
    const powershell = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], {
      cwd,
      env: { ...process.env, CLAUDE_PLUGIN_ROOT: plugin },
      input: JSON.stringify({ cwd, hook_event_name: 'SessionStart' }),
      encoding: 'utf8',
    });
    assert.equal(powershell.status, 0, powershell.stderr);
    assert.match(JSON.parse(powershell.stdout).hookSpecificOutput.additionalContext, /automation=review\b/);
  }
});
