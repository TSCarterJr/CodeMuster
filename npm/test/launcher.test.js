'use strict';

const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const launcher = require('../lib/launcher');

const DAY = 24 * 60 * 60 * 1000;

test('an explicit version pin selects an older build and never silently selects a newer one', (t) => {
  const root = tempDir();
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fakeBuild(path.join(root, '0.2.0'), process.platform);
  fakeBuild(path.join(root, '0.2.7'), process.platform);
  assert.equal(launcher.newestBuild({ versionsDir: root, platform: process.platform, pinnedVersion: '0.2.0' }).version, '0.2.0');
  assert.equal(launcher.newestBuild({ versionsDir: root, platform: process.platform, pinnedVersion: '0.2.6' }), null);
});

test('native and emulated architectures never share an update cache', () => {
  assert.notEqual(launcher.versionsDirectory('/home', 'darwin', 'arm64'), launcher.versionsDirectory('/home', 'darwin', 'x64'));
});

test('pinned builds never start automatic updates', () => {
  assert.equal(launcher.shouldCheckForUpdate({ env: { CODEMUSTER_VERSION: '0.2.0' }, now: DAY, lastCheck: null }), false);
});

test('invalid versions are refused before creating directories or fetching packages', async (t) => {
  const root = tempDir();
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  await assert.rejects(launcher.installVersion({ registry: 'http://127.0.0.1:1', version: '../escape', versionsDir: path.join(root, 'versions'), platform: process.platform, arch: process.arch }), /invalid version/);
  assert.equal(fs.existsSync(path.join(root, 'versions')), false);
});

test('release staging includes the license in the launcher and every platform package', (t) => {
  const root = tempDir();
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const builds = path.join(root, 'builds');
  const output = path.join(root, 'staged');
  const platforms = {
    'win-x64': 'win32-x64', 'win-arm64': 'win32-arm64',
    'osx-x64': 'darwin-x64', 'osx-arm64': 'darwin-arm64',
    'linux-x64': 'linux-x64', 'linux-arm64': 'linux-arm64',
  };
  for (const rid of Object.keys(platforms)) {
    fs.mkdirSync(path.join(builds, rid), { recursive: true });
    fs.writeFileSync(path.join(builds, rid, rid.startsWith('win-') ? 'codemuster.exe' : 'codemuster'), 'fixture');
  }
  require('node:child_process').execFileSync(process.execPath,
    [path.join(__dirname, '../scripts/stage.js'), '0.1.0-test', builds, output]);
  const license = fs.readFileSync(path.join(__dirname, '../../LICENSE'), 'utf8');
  for (const name of ['codemuster', ...Object.values(platforms)]) {
    const dir = path.join(output, name);
    assert.equal(fs.readFileSync(path.join(dir, 'LICENSE'), 'utf8'), license);
    assert.equal(JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8')).license, 'SEE LICENSE IN LICENSE');
    assert.equal(fs.readFileSync(path.join(dir, 'CHANGELOG.md'), 'utf8'),
      fs.readFileSync(path.join(__dirname, '../../CHANGELOG.md'), 'utf8'));
    assert.ok(JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8')).files.includes('CHANGELOG.md'));
  }
});

function tempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'codemuster-launcher-'));
}

function fakeBuild(dir, platform) {
  fs.mkdirSync(path.join(dir, 'bin'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'bin', launcher.binaryName(platform)), 'build');
}

function packTarball(version, platform, changelog) {
  const root = tempDir();
  fakeBuild(path.join(root, 'package'), platform);
  fs.writeFileSync(path.join(root, 'package', 'package.json'), JSON.stringify({ version }));
  if (changelog) fs.writeFileSync(path.join(root, 'package', 'CHANGELOG.md'), changelog);
  const tarball = path.join(root, 'build.tgz');
  launcher.tar(['-czf', tarball, '-C', root, 'package']);
  return fs.readFileSync(tarball);
}

async function fakeRegistry({ latest, version, tarball, integrity }) {
  const requests = [];
  const pkg = launcher.platformPackage(process.platform, process.arch);
  const server = http.createServer((request, response) => {
    requests.push(request.url);
    const base = `http://127.0.0.1:${server.address().port}`;
    if (request.url === '/codemuster') {
      response.end(JSON.stringify({ 'dist-tags': { latest } }));
    } else if (request.url === '/' + pkg.replace('/', '%2f')) {
      response.end(JSON.stringify({ versions: { [version]: { dist: { tarball: `${base}/build.tgz`, integrity } } } }));
    } else if (request.url === '/build.tgz') {
      response.end(tarball);
    } else {
      response.statusCode = 404;
      response.end();
    }
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return { url: `http://127.0.0.1:${server.address().port}`, requests, close: () => server.close() };
}

function integrityOf(buffer) {
  return 'sha512-' + crypto.createHash('sha512').update(buffer).digest('base64');
}

test('each supported platform maps to its build package, and others are refused', () => {
  assert.equal(launcher.platformPackage('win32', 'x64'), '@codemuster/win32-x64');
  assert.equal(launcher.platformPackage('win32', 'arm64'), '@codemuster/win32-arm64');
  assert.equal(launcher.platformPackage('darwin', 'x64'), '@codemuster/darwin-x64');
  assert.equal(launcher.platformPackage('darwin', 'arm64'), '@codemuster/darwin-arm64');
  assert.equal(launcher.platformPackage('linux', 'x64'), '@codemuster/linux-x64');
  assert.equal(launcher.platformPackage('linux', 'arm64'), '@codemuster/linux-arm64');
  assert.throws(() => launcher.platformPackage('freebsd', 'x64'), /no CodeMuster build for freebsd-x64/);
  assert.equal(launcher.binaryName('win32'), 'codemuster.exe');
  assert.equal(launcher.binaryName('linux'), 'codemuster');
});

test('versions order by number, and a release outranks its prereleases', () => {
  assert.ok(launcher.compareVersions('0.2.0', '0.1.9') > 0);
  assert.ok(launcher.compareVersions('0.10.0', '0.9.0') > 0);
  assert.ok(launcher.compareVersions('1.0.0', '0.99.99') > 0);
  assert.ok(launcher.compareVersions('0.1.0', '0.1.0-dev') > 0);
  assert.ok(launcher.compareVersions('0.1.0-dev.10', '0.1.0-dev.9') > 0);
  assert.ok(launcher.compareVersions('0.1.0-beta', '0.1.0-alpha') > 0);
  assert.equal(launcher.compareVersions('0.1.0', '0.1.0'), 0);
  assert.ok(launcher.compareVersions('0.1.0', '0.2.0') < 0);
});

test('the newest complete build wins, whether installed by npm or by an update', () => {
  const root = tempDir();
  const versionsDir = path.join(root, 'versions');
  const bundled = path.join(root, 'node_modules', 'build');
  fakeBuild(bundled, 'linux');
  fakeBuild(path.join(versionsDir, '0.2.0'), 'linux');
  fakeBuild(path.join(versionsDir, '0.1.5'), 'linux');
  fs.mkdirSync(path.join(versionsDir, '0.3.0'), { recursive: true });
  fs.mkdirSync(path.join(versionsDir, '.staging-0.4.0-abc'), { recursive: true });

  assert.deepEqual(
    launcher.newestBuild({ versionsDir, bundled: { version: '0.1.0', dir: bundled }, platform: 'linux' }),
    { version: '0.2.0', binary: path.join(versionsDir, '0.2.0', 'bin', 'codemuster') });
  assert.deepEqual(
    launcher.newestBuild({ versionsDir, bundled: { version: '0.5.0', dir: bundled }, platform: 'linux' }),
    { version: '0.5.0', binary: path.join(bundled, 'bin', 'codemuster') });
  assert.equal(launcher.newestBuild({ versionsDir: path.join(root, 'missing'), bundled: null, platform: 'linux' }), null);
});

test('updates are checked at most once a day, and never in CI or when turned off', () => {
  const now = Date.UTC(2026, 8, 11, 12);
  assert.equal(launcher.shouldCheckForUpdate({ env: {}, now, lastCheck: null }), true);
  assert.equal(launcher.shouldCheckForUpdate({ env: {}, now, lastCheck: now - DAY - 1 }), true);
  assert.equal(launcher.shouldCheckForUpdate({ env: {}, now, lastCheck: now - DAY + 60000 }), false);
  assert.equal(launcher.shouldCheckForUpdate({ env: { CI: 'true' }, now, lastCheck: null }), false);
  assert.equal(launcher.shouldCheckForUpdate({ env: { CODEMUSTER_NO_UPDATE: '1' }, now, lastCheck: null }), false);
});

test('a download that fails its integrity check is discarded', async () => {
  const tarball = packTarball('0.2.0', process.platform);
  const registry = await fakeRegistry({ latest: '0.2.0', version: '0.2.0', tarball, integrity: integrityOf(Buffer.from('something else')) });
  const versionsDir = path.join(tempDir(), 'versions');
  try {
    await assert.rejects(
      launcher.installVersion({ registry: registry.url, version: '0.2.0', versionsDir, platform: process.platform, arch: process.arch }),
      /integrity check failed/);
    assert.deepEqual(fs.readdirSync(versionsDir), []);
  } finally {
    registry.close();
  }
});

test('a newer version is unpacked beside the old ones, and only the newest two are kept', async () => {
  const tarball = packTarball('0.2.0', process.platform);
  const registry = await fakeRegistry({ latest: '0.2.0', version: '0.2.0', tarball, integrity: integrityOf(tarball) });
  const stateDir = tempDir();
  const versionsDir = launcher.versionsDirectory(stateDir, process.platform, process.arch);
  fakeBuild(path.join(versionsDir, '0.0.9'), process.platform);
  fakeBuild(path.join(versionsDir, '0.1.0'), process.platform);
  const now = Date.UTC(2026, 8, 11, 12);
  try {
    const result = await launcher.update({ registry: registry.url, stateDir, platform: process.platform, arch: process.arch, currentVersion: '0.1.0', now });

    assert.deepEqual(result, { latest: '0.2.0', installed: '0.2.0' });
    assert.deepEqual(fs.readdirSync(versionsDir).sort(), ['0.1.0', '0.2.0']);
    assert.equal(launcher.newestBuild({ versionsDir, bundled: null, platform: process.platform }).version, '0.2.0');
    assert.equal(launcher.readLastCheck(stateDir), now);
  } finally {
    registry.close();
  }
});

test('no download happens when the latest version is not newer', async () => {
  const registry = await fakeRegistry({ latest: '0.1.0', version: '0.1.0', tarball: Buffer.alloc(0), integrity: 'sha512-x' });
  const stateDir = tempDir();
  try {
    const result = await launcher.update({ registry: registry.url, stateDir, platform: process.platform, arch: process.arch, currentVersion: '0.1.0', now: 1 });

    assert.deepEqual(result, { latest: '0.1.0', installed: null });
    assert.deepEqual(registry.requests, ['/codemuster']);
  } finally {
    registry.close();
  }
});

test('the build runs with the same arguments and its exit code comes back', async () => {
  const code = await launcher.runBuild(process.execPath, ['-e', 'process.exit(Number(process.argv[1]))', '3']);

  assert.equal(code, 3);
});

test('update installs a newer version and says so', async () => {
  const tarball = packTarball('0.2.0', process.platform);
  const registry = await fakeRegistry({ latest: '0.2.0', version: '0.2.0', tarball, integrity: integrityOf(tarball) });
  const stateDir = tempDir();
  const said = [];
  try {
    const code = await launcher.updateNow({
      args: [],
      stateDir,
      platform: process.platform,
      arch: process.arch,
      currentVersion: '0.1.0',
      registry: registry.url,
      out: { write: (line) => said.push(line) },
    });

    assert.equal(code, 0);
    assert.match(said.join(''), /updating codemuster 0\.1\.0 to 0\.2\.0/);
    assert.match(said.join(''), /0\.2\.0 is ready/);
    assert.equal(launcher.newestBuild({ versionsDir: launcher.versionsDirectory(stateDir, process.platform, process.arch), bundled: null, platform: process.platform }).version, '0.2.0');
  } finally {
    registry.close();
  }
});

test('update on the newest version downloads nothing', async () => {
  const registry = await fakeRegistry({ latest: '0.1.0', version: '0.1.0', tarball: Buffer.alloc(0), integrity: 'sha512-x' });
  const stateDir = tempDir();
  const said = [];
  try {
    const code = await launcher.updateNow({
      args: [],
      stateDir,
      platform: process.platform,
      arch: process.arch,
      currentVersion: '0.1.0',
      registry: registry.url,
      out: { write: (line) => said.push(line) },
    });

    assert.equal(code, 0);
    assert.match(said.join(''), /0\.1\.0 is already the newest/);
    assert.deepEqual(registry.requests, ['/codemuster']);
    assert.ok(!fs.existsSync(launcher.versionsDirectory(stateDir, process.platform, process.arch)));
  } finally {
    registry.close();
  }
});

test('update --check says what is available without installing it', async () => {
  const tarball = packTarball('0.2.0', process.platform);
  const registry = await fakeRegistry({ latest: '0.2.0', version: '0.2.0', tarball, integrity: integrityOf(tarball) });
  const stateDir = tempDir();
  const said = [];
  try {
    const code = await launcher.updateNow({
      args: ['--check'],
      stateDir,
      platform: process.platform,
      arch: process.arch,
      currentVersion: '0.1.0',
      registry: registry.url,
      out: { write: (line) => said.push(line) },
    });

    assert.equal(code, 0);
    assert.match(said.join(''), /codemuster 0\.2\.0 is available/);
    assert.deepEqual(registry.requests, ['/codemuster']);
    assert.ok(!fs.existsSync(launcher.versionsDirectory(stateDir, process.platform, process.arch)));
  } finally {
    registry.close();
  }
});

test('update says so plainly when the registry cannot be reached', async () => {
  const stateDir = tempDir();
  const said = [];

  const code = await launcher.updateNow({
    args: [],
    stateDir,
    platform: process.platform,
    arch: process.arch,
    currentVersion: '0.1.0',
    registry: 'http://127.0.0.1:1',
    out: { write: (line) => said.push(line) },
  });

  assert.equal(code, 1);
  assert.match(said.join(''), /could not reach the npm registry/);
});

for (const args of [['update', '--help'], ['update', '-h'], ['help', 'update']]) {
  test(`${args.join(' ')} prints help without looking for or downloading a build`, async (t) => {
    const said = [];
    t.mock.method(os, 'homedir', () => { throw new Error('help must bypass build discovery'); });
    t.mock.method(process.stdout, 'write', (line) => { said.push(line); return true; });

    const code = await launcher.main(args);

    assert.equal(code, 0);
    assert.match(said.join(''), /usage: codemuster update/);
    assert.match(said.join(''), /--check/);
  });
}

const releaseHistory = `# Changelog

## Unreleased
- Not shipped yet.

## 0.4.0 - 2026-09-20
- Future change.

## 0.3.0 - 2026-09-19
- Skip oversized files.

## 0.2.0 - 2026-09-15
- Preserve audit history.

## 0.1.0 - 2026-09-11
- Old change.
`;

test('update displays all intervening release notes from the verified package', async (t) => {
  const tarball = packTarball('0.3.0', process.platform, releaseHistory);
  const registry = await fakeRegistry({ latest: '0.3.0', version: '0.3.0', tarball, integrity: integrityOf(tarball) });
  t.after(() => registry.close());
  const said = [];
  const code = await launcher.updateNow({ args: [], stateDir: tempDir(), platform: process.platform,
    arch: process.arch, currentVersion: '0.1.0', registry: registry.url, out: { write: (s) => said.push(s) } });
  assert.equal(code, 0);
  assert.match(said.join(''), /What's changed since 0\.1\.0/);
  assert.match(said.join(''), /0\.3\.0[\s\S]*Skip oversized files/);
  assert.match(said.join(''), /0\.2\.0[\s\S]*Preserve audit history/);
  assert.doesNotMatch(said.join(''), /Old change|Future change|Not shipped yet/);
});

test('packages without a changelog still update and explain that notes are unavailable', async (t) => {
  const tarball = packTarball('0.2.0', process.platform);
  const registry = await fakeRegistry({ latest: '0.2.0', version: '0.2.0', tarball, integrity: integrityOf(tarball) });
  t.after(() => registry.close());
  const said = [];
  assert.equal(await launcher.updateNow({ args: [], stateDir: tempDir(), platform: process.platform,
    arch: process.arch, currentVersion: '0.1.0', registry: registry.url, out: { write: (s) => said.push(s) } }), 0);
  assert.match(said.join(''), /release notes were not included/i);
});

function commandHarness(t, latest) {
  const home = tempDir();
  fakeBuild(path.join(launcher.versionsDirectory(path.join(home, '.codemuster'), process.platform, process.arch), '0.2.9'), process.platform);
  t.mock.method(os, 'homedir', () => home);
  const stdout = [], stderr = [], calls = [], children = [];
  const write = process.stdout.write.bind(process.stdout);
  t.mock.method(process.stdout, 'write', (line, ...args) => {
    if (typeof line === 'string' && (line.startsWith('codemuster') || line.startsWith('{"command":'))) {
      stdout.push(line);
      return true;
    }
    return write(line, ...args);
  });
  t.mock.method(process.stderr, 'write', (line) => { stderr.push(line); return true; });
  t.mock.method(global, 'fetch', async (url) => {
    calls.push(url);
    if (latest instanceof Error) throw latest;
    return { ok: true, json: async () => ({ 'dist-tags': { latest } }) };
  });
  t.mock.method(require('node:child_process'), 'spawn', (binary, args, options) => {
    children.push({ binary, args, options });
    const child = new (require('node:events').EventEmitter)();
    child.unref = () => {};
    child.kill = () => {};
    if (!options.detached) setImmediate(() => {
      process.stdout.write('{"command":"result"}\n');
      child.emit('exit', 3);
    });
    return child;
  });
  return { stdout, stderr, calls, children, stateDir: path.join(home, '.codemuster') };
}

for (const verb of ['init', 'scan', 'run', 'verify', 'fix', 'next', 'report', '--version']) {
  test(`${verb} checks availability and preserves command stdout, arguments and exit code`, async (t) => {
    const h = commandHarness(t, '0.2.10');
    fs.writeFileSync(path.join(h.stateDir, 'last-update-check'), String(Date.now()));
    assert.equal(await launcher.main([verb, '--example'], {}), 3);
    assert.deepEqual(h.calls, [launcher.REGISTRY + '/codemuster']);
    assert.match(h.stderr.join(''), /0\.2\.10 is available.*0\.2\.9.*codemuster update/);
    assert.deepEqual(h.stdout, ['{"command":"result"}\n']);
    assert.equal(h.children.length, 1);
    assert.deepEqual(h.children[0].args, [verb, '--example']);
  });
}

for (const [latest, expected] of [
  ['0.2.9', /0\.2\.9 is up to date/],
  ['0.2.8', /0\.2\.9 is newer than.*0\.2\.8/],
  [new Error('offline'), /update check unavailable.*continuing with 0\.2\.9/],
  ['bad-version', /update check unavailable/],
  [null, /update check unavailable/],
]) {
  test(`normal commands report version status honestly for ${latest}`, async (t) => {
    const h = commandHarness(t, latest);
    assert.equal(await launcher.main(['scan'], {}), 3);
    assert.match(h.stderr.join(''), expected);
    assert.equal(h.children.length, 1);
  });
}

for (const env of [{ CI: '1' }, { CODEMUSTER_NO_UPDATE: '1' }, { CODEMUSTER_VERSION: '0.2.9' }]) {
  test(`normal command respects ${Object.keys(env)[0]}`, async (t) => {
    const h = commandHarness(t, '0.2.10');
    assert.equal(await launcher.main(['scan'], env), 3);
    assert.deepEqual(h.calls, []);
    assert.deepEqual(h.stderr, []);
    assert.equal(h.children.length, 1);
  });
}

test('available updates still install in the background when the daily interval is due', async (t) => {
  const h = commandHarness(t, '0.2.10');
  assert.equal(await launcher.main(['scan'], {}), 3);
  assert.match(h.stderr.join(''), /0\.2\.10 is available/);
  assert.equal(h.children.filter((c) => c.options.detached).length, 1);
  assert.equal(h.children.filter((c) => !c.options.detached).length, 1);
});

test('a slow availability check times out without claiming the installed version is current', async (t) => {
  const server = http.createServer(() => {});
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { server.closeAllConnections(); server.close(); });
  const said = [];
  const result = await launcher.checkForUpdate({ currentVersion: '0.2.9',
    registry: `http://127.0.0.1:${server.address().port}`, timeoutMs: 30, out: { write: (s) => said.push(s) } });
  assert.equal(result, null);
  assert.match(said.join(''), /update check unavailable.*continuing with 0\.2\.9/);
  assert.doesNotMatch(said.join(''), /up to date/);
});

test('stable package staging refuses a version with no changelog entry before touching output', () => {
  const root = tempDir();
  const output = path.join(root, 'output');
  fs.mkdirSync(output);
  fs.writeFileSync(path.join(output, 'keep.txt'), 'retained');
  const result = require('node:child_process').spawnSync(process.execPath,
    [path.join(__dirname, '../scripts/stage.js'), '999.0.0', path.join(root, 'builds'), output], { encoding: 'utf8' });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /changelog.*999\.0\.0/i);
  assert.equal(fs.readFileSync(path.join(output, 'keep.txt'), 'utf8'), 'retained');
});
