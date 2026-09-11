'use strict';

const fs = require('node:fs');
const path = require('node:path');

const PLATFORMS = {
  'win-x64': ['win32', 'x64'],
  'win-arm64': ['win32', 'arm64'],
  'osx-x64': ['darwin', 'x64'],
  'osx-arm64': ['darwin', 'arm64'],
  'linux-x64': ['linux', 'x64'],
  'linux-arm64': ['linux', 'arm64'],
};

const [version, buildsDir, outDir] = process.argv.slice(2);
if (!version || !buildsDir || !outDir) {
  process.stderr.write('usage: node stage.js <version> <folder holding one build per RID> <output folder>\n');
  process.exit(2);
}

const launcherDir = path.join(__dirname, '..');
const launcher = JSON.parse(fs.readFileSync(path.join(launcherDir, 'package.json'), 'utf8'));
fs.rmSync(outDir, { recursive: true, force: true });

const staged = [];
for (const [rid, [os, cpu]] of Object.entries(PLATFORMS)) {
  const build = path.join(buildsDir, rid);
  if (!fs.existsSync(build)) {
    continue;
  }

  const name = `@codemuster/${os}-${cpu}`;
  const dir = path.join(outDir, `${os}-${cpu}`);
  fs.cpSync(build, path.join(dir, 'bin'), { recursive: true });
  if (os !== 'win32') {
    fs.chmodSync(path.join(dir, 'bin', 'codemuster'), 0o755);
  }

  fs.writeFileSync(path.join(dir, 'package.json'), JSON.stringify({
    name,
    version,
    description: `The ${os}-${cpu} build of CodeMuster, installed by the codemuster package.`,
    license: launcher.license,
    repository: launcher.repository,
    os: [os],
    cpu: [cpu],
    files: ['bin'],
  }, null, 2) + '\n');
  staged.push(name);
}

if (staged.length === 0) {
  process.stderr.write(`no builds found under ${buildsDir}\n`);
  process.exit(1);
}

const dir = path.join(outDir, 'codemuster');
for (const entry of ['bin', 'lib', 'README.md']) {
  fs.cpSync(path.join(launcherDir, entry), path.join(dir, entry), { recursive: true });
}

const optionalDependencies = Object.fromEntries(Object.keys(launcher.optionalDependencies).map((name) => [name, version]));
fs.writeFileSync(path.join(dir, 'package.json'), JSON.stringify({ ...launcher, version, optionalDependencies }, null, 2) + '\n');
process.stdout.write([...staged, 'codemuster'].map((name) => `staged ${name}@${version}`).join('\n') + '\n');
