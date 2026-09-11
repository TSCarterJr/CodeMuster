#!/usr/bin/env node
'use strict';

const launcher = require('../lib/launcher');

launcher.main(process.argv.slice(2)).then(
  (code) => process.exit(code),
  (error) => {
    process.stderr.write(`codemuster: ${error.message}\n`);
    process.exit(1);
  });
