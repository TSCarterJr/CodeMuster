'use strict';

const fs = require('node:fs');
const path = require('node:path');
const launcher = require('./launcher');

const [stateDir, currentVersion] = process.argv.slice(2);

launcher
  .update({ registry: launcher.REGISTRY, stateDir, platform: process.platform, arch: process.arch, currentVersion, now: Date.now() })
  .catch((error) => {
    try {
      fs.writeFileSync(path.join(stateDir, 'last-update-error'), String((error && error.stack) || error));
    } catch {
    }
  });
