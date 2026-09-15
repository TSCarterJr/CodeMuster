'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { parseArgs } = require('node:util');

const repo = path.resolve(__dirname, '..');
let options;
let manifest;
try {
  options = parseArgs({ options: { check: { type: 'boolean' }, out: { type: 'string' }, version: { type: 'string' } } }).values;
  manifest = JSON.parse(fs.readFileSync(path.join(repo, 'distribution/plugin.json'), 'utf8'));
  manifest.version = options.version ?? manifest.version;
  if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?$/.test(manifest.version)) {
    throw new Error(`invalid plugin version: ${manifest.version}`);
  }
} catch (error) {
  process.stderr.write(`${error.message}\nusage: node scripts/stage-plugin.js [--check] [--out <directory>] [--version <version>]\n`);
  process.exit(2);
}

const output = path.resolve(options.out ?? repo);
const { $schema, extensions, ...identity } = manifest;
const presentation = extensions['com.openai'].interface;
const pluginPath = 'plugins/codemuster';
const files = new Map();
const json = (value) => JSON.stringify(value, null, 2) + '\n';
files.set(`${pluginPath}/plugin.json`, json(manifest));
files.set(`${pluginPath}/.claude-plugin/plugin.json`, json(identity));
files.set(`${pluginPath}/.codex-plugin/plugin.json`, json({ ...identity, skills: './skills/', interface: presentation }));
files.set('.claude-plugin/marketplace.json', json({
  name: 'codemuster',
  owner: identity.author,
  description: 'CodeMuster codebase audits, verification, and fixes.',
  plugins: [{ name: identity.name, source: `./${pluginPath}`, description: identity.description, category: 'development' }],
}));
files.set('.agents/plugins/marketplace.json', json({
  name: 'codemuster',
  interface: { displayName: 'CodeMuster' },
  plugins: [{
    name: identity.name,
    source: { source: 'local', path: `./${pluginPath}` },
    policy: { installation: 'AVAILABLE', authentication: 'ON_INSTALL' },
    category: presentation.category,
  }],
}));
for (const [destination, source] of [
  [`${pluginPath}/skills/codemuster/SKILL.md`, 'skill/SKILL.md'],
  [`${pluginPath}/skills/codemuster/LICENSE`, 'LICENSE'],
  [`${pluginPath}/LICENSE`, 'LICENSE'],
  [`${pluginPath}/README.md`, 'distribution/README.md'],
  [`${pluginPath}/hooks/hooks.json`, 'distribution/hooks/hooks.json'],
  [`${pluginPath}/hooks/context.cjs`, 'distribution/hooks/context.cjs'],
]) {
  files.set(destination, fs.readFileSync(path.join(repo, source)));
}

for (const [relative, content] of files) {
  const target = path.join(output, relative);
  const expected = Buffer.from(content);
  if (fs.existsSync(target) && fs.readFileSync(target).equals(expected)) continue;
  if (options.check) {
    process.stderr.write(`stale or missing: ${relative}; run node scripts/stage-plugin.js\n`);
    process.exitCode = 1;
  } else {
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, expected);
  }
}
if (!process.exitCode) process.stdout.write(`plugin ${manifest.version}: ${options.check ? 'verified' : 'staged'} ${files.size} files\n`);
