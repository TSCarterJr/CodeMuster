'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const root = path.resolve(__dirname, '../..');

test('public workflows use hosted runners and publishing requires same-revision validation', () => {
  const testWorkflow = fs.readFileSync(path.join(root, '.github/workflows/test.yml'), 'utf8');
  const release = fs.readFileSync(path.join(root, '.github/workflows/release.yml'), 'utf8');
  assert.doesNotMatch(testWorkflow + release, /self-hosted/);
  assert.match(testWorkflow, /workflow_call:/);
  assert.match(release, /uses: \.\/\.github\/workflows\/test.yml/);
  assert.match(release, /build:\s+needs: validate/);
  assert.match(release, /scripts\/smoke-package.js/);
  assert.match(release, /scripts\/publish-packages.js/);
});

test('publication resumes identical packages but refuses changed bytes and registry failures', async () => {
  const { publishPackages } = require('../../scripts/publish-packages');
  const packages = [{ name: '@codemuster/linux-x64', version: '0.2.7', integrity: 'sha512-one', file: 'platform.tgz' },
    { name: 'codemuster', version: '0.2.7', integrity: 'sha512-two', file: 'launcher.tgz' }];
  const published = [];
  await publishPackages(packages, async (pkg) => pkg.name.startsWith('@') ? 'sha512-one' : null,
    async (pkg) => published.push(pkg.file));
  assert.deepEqual(published, ['launcher.tgz']);
  await assert.rejects(publishPackages(packages, async () => 'sha512-different', async () => assert.fail('must not publish')), /integrity/);
  await assert.rejects(publishPackages(packages, async () => { throw new Error('registry unavailable'); }, async () => assert.fail('must not publish')), /registry unavailable/);
});
