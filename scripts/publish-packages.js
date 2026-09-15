'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');

async function publishPackages(packages, lookup, publish) {
  const missing = [];
  for (const pkg of packages) {
    const integrity = await lookup(pkg);
    if (integrity === null) missing.push(pkg);
    else if (integrity !== pkg.integrity) throw new Error(`integrity mismatch for ${pkg.name}@${pkg.version}; retain original release artifacts or choose a new version`);
  }
  for (const pkg of missing) await publish(pkg);
}

async function main(directory, version) {
  if (!directory || !/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version || '')) throw new Error('usage: node scripts/publish-packages.js <packages-directory> <version>');
  const platforms = ['darwin-arm64', 'darwin-x64', 'linux-arm64', 'linux-x64', 'win32-arm64', 'win32-x64'];
  const packages = [...platforms.map(p => [`@codemuster/${p}`, `codemuster-${p}`]), ['codemuster', 'codemuster']].map(([name, filename]) => {
    const file = path.resolve(directory, `${filename}-${version}.tgz`);
    const manifest = JSON.parse(execFileSync('tar', ['-xOf', file, 'package/package.json'], { encoding: 'utf8' }));
    if (manifest.name !== name || manifest.version !== version) throw new Error(`wrong package identity in ${file}`);
    return { name, version, file, integrity: 'sha512-' + crypto.createHash('sha512').update(fs.readFileSync(file)).digest('base64') };
  });
  await publishPackages(packages, async pkg => {
    const response = await fetch(`https://registry.npmjs.org/${encodeURIComponent(pkg.name)}/${pkg.version}`, { signal: AbortSignal.timeout(30000) });
    if (response.status === 404) return null;
    if (!response.ok) throw new Error(`registry lookup failed for ${pkg.name}: HTTP ${response.status}`);
    return (await response.json()).dist.integrity;
  }, async pkg => {
    execFileSync('npm', ['publish', pkg.file, '--access', 'public', '--tag', version.includes('-') ? 'next' : 'latest'], { stdio: 'inherit' });
  });
  process.stdout.write(`verified/published all seven packages at ${version}\n`);
}

module.exports = { publishPackages };
if (require.main === module) main(...process.argv.slice(2)).catch(error => { process.stderr.write(error.message + '\n'); process.exitCode = 1; });
