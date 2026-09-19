'use strict';

function releases(text) {
  return text.replace(/\r\n/g, '\n').split(/^## /m).slice(1).flatMap((section) => {
    const match = /^(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)(?: - [^\n]+)?\n([\s\S]*)$/.exec(section);
    return match && match[2].trim() ? [{ version: match[1], body: match[2].trim() }] : [];
  });
}

module.exports = { releases };
