'use strict';

const childProcess = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { releases } = require('./changelog');

const REGISTRY = 'https://registry.npmjs.org';
const DAY = 24 * 60 * 60 * 1000;
const KEEP_VERSIONS = 2;
const PLATFORMS = new Set(['win32-x64', 'win32-arm64', 'darwin-x64', 'darwin-arm64', 'linux-x64', 'linux-arm64']);
const VERSION = /^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/;

function versionsDirectory(stateDir, platform, arch) {
  return path.join(stateDir, `${platform}-${arch}`, 'versions');
}

function platformPackage(platform, arch) {
  const name = `${platform}-${arch}`;
  if (!PLATFORMS.has(name)) {
    throw new Error(`there is no CodeMuster build for ${name}`);
  }

  return `@codemuster/${name}`;
}

function binaryName(platform) {
  return platform === 'win32' ? 'codemuster.exe' : 'codemuster';
}

function compareVersions(a, b) {
  const [coreA, preA] = splitVersion(a);
  const [coreB, preB] = splitVersion(b);
  for (let i = 0; i < 3; i++) {
    const difference = Number(coreA[i]) - Number(coreB[i]);
    if (difference !== 0) {
      return difference;
    }
  }

  if (preA === null || preB === null) {
    return preA === preB ? 0 : preA === null ? 1 : -1;
  }

  const partsA = preA.split('.');
  const partsB = preB.split('.');
  for (let i = 0; i < Math.max(partsA.length, partsB.length); i++) {
    if (partsA[i] === undefined || partsB[i] === undefined) {
      return partsA[i] === undefined ? -1 : 1;
    }

    const numericA = /^\d+$/.test(partsA[i]);
    const numericB = /^\d+$/.test(partsB[i]);
    if (numericA && numericB && Number(partsA[i]) !== Number(partsB[i])) {
      return Number(partsA[i]) - Number(partsB[i]);
    }

    if (numericA !== numericB) {
      return numericA ? -1 : 1;
    }

    if (partsA[i] !== partsB[i]) {
      return partsA[i] < partsB[i] ? -1 : 1;
    }
  }

  return 0;
}

function splitVersion(version) {
  const dash = version.indexOf('-');
  const core = dash < 0 ? version : version.slice(0, dash);
  return [core.split('.'), dash < 0 ? null : version.slice(dash + 1)];
}

function newestBuild({ versionsDir, bundled, platform, pinnedVersion }) {
  const candidates = bundled ? [{ version: bundled.version, binary: path.join(bundled.dir, 'bin', binaryName(platform)) }] : [];
  for (const version of installedVersions(versionsDir)) {
    candidates.push({ version, binary: path.join(versionsDir, version, 'bin', binaryName(platform)) });
  }

  const complete = candidates.filter((candidate) => (!pinnedVersion || candidate.version === pinnedVersion) && fs.existsSync(candidate.binary));
  complete.sort((x, y) => compareVersions(y.version, x.version));
  return complete[0] ?? null;
}

function installedVersions(versionsDir) {
  try {
    return fs.readdirSync(versionsDir).filter((entry) => VERSION.test(entry));
  } catch {
    return [];
  }
}

function shouldCheckForUpdate({ env, now, lastCheck }) {
  return !env.CI && !env.CODEMUSTER_NO_UPDATE && !env.CODEMUSTER_VERSION && (lastCheck === null || now - lastCheck >= DAY);
}

function readLastCheck(stateDir) {
  try {
    const value = Number(fs.readFileSync(path.join(stateDir, 'last-update-check'), 'utf8'));
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function tar(args) {
  // Git for Windows puts GNU tar first on PATH, and GNU tar reads "C:" in a path as a remote host.
  const command = process.platform === 'win32' ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe') : 'tar';
  childProcess.execFileSync(command, args, { stdio: 'pipe' });
}

async function getJson(url, timeoutMs = 60000) {
  const response = await fetch(url, { signal: AbortSignal.timeout(timeoutMs), headers: { accept: 'application/vnd.npm.install-v1+json; q=1.0, application/json; q=0.8' } });
  if (!response.ok) {
    throw new Error(`GET ${url} returned ${response.status}`);
  }

  return response.json();
}

async function checkForUpdate({ currentVersion, registry = REGISTRY, timeoutMs = 2000, out = process.stderr }) {
  try {
    const packument = await getJson(`${registry}/codemuster`, timeoutMs);
    const latest = packument['dist-tags']?.latest;
    if (typeof latest !== 'string' || !VERSION.test(latest)) throw new Error('the registry named no valid latest version');
    const comparison = compareVersions(latest, currentVersion);
    if (comparison > 0) {
      out.write(`codemuster ${latest} is available; running ${currentVersion}. Run codemuster update to install it.\n`);
    } else if (comparison === 0) {
      out.write(`codemuster ${currentVersion} is up to date\n`);
    } else {
      out.write(`codemuster ${currentVersion} is newer than the latest published version (${latest})\n`);
    }
    return latest;
  } catch {
    out.write(`codemuster: update check unavailable; continuing with ${currentVersion}\n`);
    return null;
  }
}

function showChanges(dir, currentVersion, latest, out) {
  let entries = [];
  try {
    entries = releases(fs.readFileSync(path.join(dir, 'CHANGELOG.md'), 'utf8'))
      .filter((entry) => compareVersions(entry.version, currentVersion) > 0 && compareVersions(entry.version, latest) <= 0)
      .sort((a, b) => compareVersions(b.version, a.version));
  } catch {
  }
  if (entries.length === 0) {
    out.write('Release notes were not included in this package.\n');
    return;
  }
  out.write(`\nWhat's changed since ${currentVersion}:\n\n`);
  for (const entry of entries) out.write(`${entry.version}\n${entry.body}\n\n`);
}

async function installVersion({ registry, version, versionsDir, platform, arch }) {
  if (!VERSION.test(version)) throw new Error(`invalid version: ${version}`);
  const pkg = platformPackage(platform, arch);
  fs.mkdirSync(versionsDir, { recursive: true });
  const packument = await getJson(`${registry}/${pkg.replace('/', '%2f')}`);
  const dist = packument.versions && packument.versions[version] && packument.versions[version].dist;
  if (!dist) {
    throw new Error(`${pkg}@${version} is not in the registry`);
  }

  const response = await fetch(dist.tarball, { signal: AbortSignal.timeout(60000) });
  if (!response.ok) {
    throw new Error(`GET ${dist.tarball} returned ${response.status}`);
  }

  const tarball = Buffer.from(await response.arrayBuffer());
  const expected = /sha512-([A-Za-z0-9+\/=]+)/.exec(dist.integrity || '');
  if (!expected || crypto.createHash('sha512').update(tarball).digest('base64') !== expected[1]) {
    throw new Error(`integrity check failed for ${pkg}@${version}`);
  }

  const target = path.join(versionsDir, version);
  const staging = fs.mkdtempSync(path.join(versionsDir, `.staging-${version}-`));
  try {
    const file = path.join(staging, 'build.tgz');
    fs.writeFileSync(file, tarball);
    tar(['-xzf', file, '-C', staging]);
    const binary = path.join(staging, 'package', 'bin', binaryName(platform));
    if (!fs.existsSync(binary)) {
      throw new Error(`${pkg}@${version} has no bin/${binaryName(platform)}`);
    }

    if (platform !== 'win32') {
      fs.chmodSync(binary, 0o755);
    }

    try {
      fs.renameSync(path.join(staging, 'package'), target);
    } catch (error) {
      if (!fs.existsSync(target)) {
        throw error;
      }
    }
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }

  return target;
}

function pruneVersions(versionsDir) {
  const versions = installedVersions(versionsDir).sort((x, y) => compareVersions(y, x));
  for (const version of versions.slice(KEEP_VERSIONS)) {
    try {
      fs.rmSync(path.join(versionsDir, version), { recursive: true, force: true });
    } catch {
    }
  }
}

async function update({ registry, stateDir, platform, arch, currentVersion, now }) {
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, 'last-update-check'), String(now));
  const packument = await getJson(`${registry}/codemuster`);
  const latest = (packument['dist-tags'] && packument['dist-tags'].latest) || null;
  if (latest === null || compareVersions(latest, currentVersion) <= 0) {
    return { latest, installed: null };
  }

  const versionsDir = versionsDirectory(stateDir, platform, arch);
  await installVersion({ registry, version: latest, versionsDir, platform, arch });
  pruneVersions(versionsDir);
  return { latest, installed: latest };
}

function runBuild(binary, args) {
  return new Promise((resolve, reject) => {
    const child = childProcess.spawn(binary, args, { stdio: 'inherit' });
    // Windows delivers Ctrl+C to the console group; forwarding it would force-kill the native build.
    const handlers = ['SIGINT', 'SIGTERM', 'SIGHUP'].map((signal) => [signal, () => {
      if (signal !== 'SIGINT' || process.platform !== 'win32') child.kill(signal);
    }]);
    handlers.forEach(([signal, handler]) => process.on(signal, handler));
    const done = () => handlers.forEach(([signal, handler]) => process.off(signal, handler));
    child.on('error', (error) => {
      done();
      reject(error);
    });
    child.on('exit', (code) => {
      done();
      resolve(code ?? 1);
    });
  });
}

function bundledBuild(pkg) {
  try {
    const manifest = require.resolve(`${pkg}/package.json`);
    return { version: JSON.parse(fs.readFileSync(manifest, 'utf8')).version, dir: path.dirname(manifest) };
  } catch {
    return null;
  }
}

async function updateNow({ args, stateDir, platform, arch, currentVersion, registry = REGISTRY, out = process.stdout }) {
  const checkOnly = args.includes('--check');
  let packument;
  try {
    packument = await getJson(`${registry}/codemuster`);
  } catch (error) {
    out.write(`codemuster: could not reach the npm registry: ${error.message}\n`);
    return 1;
  }
  const latest = (packument['dist-tags'] && packument['dist-tags'].latest) || null;
  if (latest === null) {
    out.write('codemuster: the registry named no latest version\n');
    return 1;
  }

  if (compareVersions(latest, currentVersion) <= 0) {
    out.write(`codemuster ${currentVersion} is already the newest\n`);
    return 0;
  }

  if (checkOnly) {
    out.write(`codemuster ${latest} is available; you are on ${currentVersion}. Run codemuster update to install it.\n`);
    return 0;
  }

  out.write(`updating codemuster ${currentVersion} to ${latest}\n`);
  const versionsDir = versionsDirectory(stateDir, platform, arch);
  let installedDir;
  try {
    installedDir = await installVersion({ registry, version: latest, versionsDir, platform, arch });
  } catch (error) {
    out.write(`codemuster: ${latest} could not be installed: ${error.message}\n`);
    return 1;
  }
  pruneVersions(versionsDir);
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, 'last-update-check'), String(Date.now()));
  out.write(`codemuster ${latest} is ready; the next command uses it\n`);
  showChanges(installedDir, currentVersion, latest, out);
  return 0;
}

async function main(args, env = process.env) {
  if ((args[0] === 'update' && args.slice(1).some((arg) => arg === '--help' || arg === '-h'))
      || (args.length === 2 && args[0] === 'help' && args[1] === 'update')) {
    process.stdout.write('usage: codemuster update [--check]\n\nUpdate CodeMuster itself and show the intervening release notes.\n  --check   Show the available version without installing it\n\nNormal commands check availability with a two-second timeout; hook and commands in CodeMuster workers do not.\nAutomatic checks and background updates can be disabled with CI or CODEMUSTER_NO_UPDATE.\n');
    return 0;
  }

  const platform = process.platform;
  const arch = process.arch;
  const stateDir = path.join(os.homedir(), '.codemuster');
  const versionsDir = versionsDirectory(stateDir, platform, arch);
  const pinnedVersion = env.CODEMUSTER_VERSION;
  if (pinnedVersion && !VERSION.test(pinnedVersion)) throw new Error(`invalid version pin: ${pinnedVersion}`);
  let build = newestBuild({ versionsDir, pinnedVersion, bundled: bundledBuild(platformPackage(platform, arch)), platform });
  if (build === null) {
    const version = pinnedVersion || require('../package.json').version;
    process.stderr.write(`codemuster: downloading CodeMuster ${version} for ${platform}-${arch}\n`);
    await installVersion({ registry: REGISTRY, version, versionsDir, platform, arch });
    build = newestBuild({ versionsDir, pinnedVersion, bundled: null, platform });
  }

  if (args[0] === 'update') {
    if (pinnedVersion) {
      process.stderr.write(`codemuster is pinned to ${pinnedVersion}; unset CODEMUSTER_VERSION before updating\n`);
      return 1;
    }
    return updateNow({ args: args.slice(1), stateDir, platform, arch, currentVersion: build.version });
  }

  // hook fires after every agent edit and workers are CodeMuster's own agent processes; neither is a command someone typed.
  const automated = args[0] === 'hook' || env.CODEMUSTER_WORKER !== undefined;
  if (!env.CI && !env.CODEMUSTER_NO_UPDATE && !pinnedVersion && !automated) {
    const latest = await checkForUpdate({ currentVersion: build.version });
    if (latest && compareVersions(latest, build.version) > 0
        && shouldCheckForUpdate({ env, now: Date.now(), lastCheck: readLastCheck(stateDir) })) {
      try {
        childProcess
          .spawn(process.execPath, [path.join(__dirname, 'update.js'), stateDir, build.version], { detached: true, stdio: 'ignore', windowsHide: true })
          .on('error', () => {})
          .unref();
      } catch {
      }
    }
  }

  return runBuild(build.binary, args);
}

module.exports = {
  REGISTRY,
  versionsDirectory,
  binaryName,
  compareVersions,
  checkForUpdate,
  installVersion,
  main,
  newestBuild,
  platformPackage,
  readLastCheck,
  runBuild,
  shouldCheckForUpdate,
  tar,
  update,
  updateNow,
};
