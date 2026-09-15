'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const launcher = path.resolve(process.argv[2] || 'missing-launcher');
assert.ok(fs.existsSync(launcher), `launcher does not exist: ${launcher}`);
const repo = path.resolve(__dirname, '..');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'codemuster-package-smoke-'));
const fixture = path.join(root, 'fixture');
const source = path.join(repo, 'fixtures/mixed-repo');
fs.cpSync(source, fixture, { recursive: true, filter: entry => !path.relative(source, entry).split(path.sep).some(p => ['bin', 'obj', 'node_modules', '.git', '.codemuster'].includes(p)) });

function run(executable, args, env = {}) {
  if (executable === 'npm' && process.platform === 'win32') {
    args = [path.join(path.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js'), ...args];
    executable = process.execPath;
  }
  const result = spawnSync(executable, args, { cwd: fixture, encoding: 'utf8', timeout: 180000, maxBuffer: 16 * 1024 * 1024,
    env: { ...process.env, CODEMUSTER_NO_UPDATE: '1', ...env } });
  assert.equal(result.status, 0, `${executable} ${args.join(' ')}\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
  return result;
}
const cli = (...args) => run(process.execPath, [launcher, ...args], { CODEMUSTER_FAKE_RESPONSE: path.join(root, 'response.json') });

try {
  run('git', ['init', '-q']);
  run('git', ['config', 'user.name', 'CodeMuster package smoke']);
  run('git', ['config', 'user.email', 'smoke@codemuster.invalid']);
  run('git', ['config', 'commit.gpgsign', 'false']);
  run('git', ['add', '--', '.']);
  run('git', ['commit', '-qm', 'fixture']);
  run('dotnet', ['restore', 'MixedRepo.sln']);
  run('npm', ['ci', '--prefix', 'web']);
  cli('init', '--yes', '--no-skills');
  const configPath = path.join(fixture, '.codemuster/config.json');
  const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
  config.vulnerabilities = false;
  config.test_command = ['dotnet', 'build', 'MixedRepo.sln', '--no-restore'];
  fs.writeFileSync(configPath, JSON.stringify(config));
  const scan = cli('scan');
  assert.doesNotMatch(scan.stderr, /mapping failed|not restored|could not run/i);
  const status = cli('status').stdout;
  assert.match(status, /slice/);
  cli('scan', '--mode', 'file');
  fs.writeFileSync(path.join(root, 'response.json'), JSON.stringify({ summary: 'deterministic package smoke', findings: [{
    path: 'src/MixedRepo.Api/Program.cs', line_start: 1, line_end: 1, severity: 'low', category: 'smoke',
    claim: 'Synthetic engine fixture', evidence: 'Synthetic fixture, not a defect claim', confidence: 0.9, lens_id: 'default',
  }] }));
  const target = 'src/MixedRepo.Api/Program.cs';
  cli('run', '--agent', 'fake', '--path', target);
  cli('verify', '--agent', 'fake', '--path', target);
  cli('fix', '--agent', 'fake', '--path', target);
  assert.match(cli('report').stdout, /fix: fixed/);
  cli('validate');
  assert.equal(run('git', ['status', '--porcelain', '--untracked-files=no']).stdout.trim(), '');
  process.stdout.write('packaged CLI passed init, C#/TS scan, ledger reopen, audit, verification, isolated fix and actual fixture build validation\n');
} catch (error) {
  process.stderr.write(`package smoke evidence retained at ${root}\n`);
  throw error;
}
