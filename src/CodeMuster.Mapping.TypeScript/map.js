'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const chunks = [];
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => chunks.push(chunk));
process.stdin.on('end', () => {
  try {
    const request = JSON.parse(chunks.join(''));
    const programs = [];
    for (const tsconfig of request.tsconfigs.length === 0 ? [null] : request.tsconfigs) {
      const loaded = loadTypeScript(request.repo_root, tsconfig, request.paths);
      // Loose JavaScript with no config asked for no mapping, so a missing compiler leaves it whole-file instead of failing the map.
      if (loaded.missing && tsconfig === null && !request.paths.some((file) => TYPESCRIPT.test(file))) {
        process.stdout.write(JSON.stringify({
          symbols: [],
          edges: [],
          entry_points: [],
          resolution: { resolved: 0, unresolved: 0, top_unresolved_names: [] },
          diagnostics: [],
          skipped_languages: [{ language: 'javascript', note: LOOSE_NOTE }],
        }));
        return;
      }

      if (loaded.error !== undefined) {
        fail(loaded.error);
        return;
      }

      programs.push({ tsconfig, ts: loaded.ts, module: loaded.module });
    }

    process.stdout.write(JSON.stringify(mapRepo(request, programs)));
  } catch (error) {
    fail(describe(error));
  }
});

function fail(message) {
  process.stderr.write(`${message}\n`);
  process.exitCode = 1;
}

// This script runs from a temporary copy that is deleted before anyone reads the error, so name its line instead of its path.
// Only the message's first line is kept: Node's "Require stack" continues it with paths, this script's among them.
function describe(error) {
  const message = firstLine(error);
  const frame = error instanceof Error
    ? String(error.stack).split('\n').find((line) => line.trim().startsWith('at ') && line.includes(__filename))
    : undefined;
  const line = frame === undefined ? null : /:(\d+):\d+\)?$/.exec(frame.trim());
  return line === null ? message : `${message} (map.js line ${line[1]})`;
}

function firstLine(error) {
  return (error instanceof Error ? error.message : String(error)).split('\n')[0].trim();
}

// Synchronous so each line reaches the parent while mapping runs, not in one burst after it.
function report(message) {
  fs.writeSync(2, `progress: ${message}\n`);
}

// TypeScript 7 ships a native compiler with no JavaScript API; Microsoft publishes the TypeScript 6 API beside it, so the first of the two
// packages that resolves and has createProgram maps the project.
// A null tsconfig is the default program (D64): typescript is looked up from the repository root, then from each script's folder, and the fix is
// named for the first of those with a package.json on disk (scan never passes package.json, which it excludes as data).
function loadTypeScript(repoRoot, tsconfig, paths) {
  const folders = tsconfig === null ? ['.', ...new Set(paths.filter((file) => SCRIPT.test(file)).map((file) => path.posix.dirname(file)).sort(ordinal))] : [];
  const folder = tsconfig === null ? packageFolder(repoRoot, folders) : path.posix.dirname(tsconfig);
  const directory = path.resolve(repoRoot, folder);
  const directories = tsconfig === null ? folders.map((member) => path.resolve(repoRoot, member)) : [directory];
  const subject = tsconfig ?? 'the JavaScript and TypeScript files';
  // npm's --prefix on a workspace member writes a second lockfile there, so npm's command is named only where the folder has its own.
  const hint = (npm, other) => (fs.existsSync(path.join(directory, 'package-lock.json'))
    ? `run ${npm}${tsconfig === null && folder === '.' ? '' : ` --prefix ${folder}`}`
    : other);
  const candidates = ['typescript', '@typescript/typescript6']
    .map((name) => ({ name, file: resolvePackage(name, directories) }))
    .filter((candidate) => candidate.file !== undefined);
  if (candidates.length === 0) {
    return {
      missing: true,
      error: tsconfig === null
        ? `typescript was not found for ${subject}; ${hint('npm i -D typescript', `add typescript as a dev dependency of ${folder === '.' ? 'the repository' : folder} with its package manager`)}`
        : `typescript was not found for ${tsconfig}; ${hint('npm ci', `install the dependencies of ${folder} with its package manager`)}`,
    };
  }

  let version;
  for (const candidate of candidates) {
    let ts;
    try {
      ts = require(candidate.file);
    } catch (error) {
      return { error: `${subject}: ${candidate.name} could not be loaded (${firstLine(error)}); ${hint('npm ci', `reinstall the dependencies of ${folder} with its package manager`)}` };
    }

    if (typeof ts.createProgram === 'function') {
      return { ts, module: candidate.file };
    }

    version ??= ts.version;
  }

  return {
    error: `${subject}: typescript ${version} has no JavaScript compiler API; `
      + hint('npm i -D @typescript/typescript6', `add @typescript/typescript6 as a dev dependency of ${folder} with its package manager`),
  };
}

function packageFolder(repoRoot, folders) {
  for (const start of folders) {
    for (let folder = start; ; folder = path.posix.dirname(folder)) {
      if (fs.existsSync(path.join(repoRoot, folder, 'package.json'))) {
        return folder;
      }

      if (folder === '.') {
        break;
      }
    }
  }

  return '.';
}

function resolvePackage(name, paths) {
  try {
    return require.resolve(name, { paths });
  } catch {
    return undefined;
  }
}

const SCRIPT = /\.(ts|tsx|mts|cts|js|jsx|mjs|cjs)$/;
const TYPESCRIPT = /\.(ts|tsx|mts|cts)$/i;
const LOOSE_NOTE = 'javascript: not mapped (no tsconfig, jsconfig or typescript package); files are reviewed whole. Add typescript as a dev dependency to map them';

// D64: with no tsconfig.json or jsconfig.json, one program over every included script, reading JavaScript without type-checking it.
function createProgram(ts, repoRoot, tsconfig, included, parse) {
  if (tsconfig === null) {
    const rootNames = included.filter((file) => SCRIPT.test(file)).map((file) => path.join(repoRoot, file));
    report(`loading ${rootNames.length} files without a tsconfig.json or jsconfig.json`);
    const options = {
      allowJs: true,
      checkJs: false,
      noEmit: true,
      skipLibCheck: true,
      jsx: ts.JsxEmit.Preserve,
      target: ts.ScriptTarget.ES2022,
      module: ts.ModuleKind.ESNext,
      moduleResolution: ts.ModuleResolutionKind.Bundler ?? ts.ModuleResolutionKind.NodeJs,
    };
    return { program: ts.createProgram({ rootNames, options, host: parse(options) }), errors: [] };
  }

  report(`loading ${tsconfig}`);
  const configPath = path.join(repoRoot, tsconfig);
  const config = ts.readConfigFile(configPath, ts.sys.readFile);
  const parsed = ts.parseJsonConfigFileContent(config.config || {}, ts.sys, path.dirname(configPath), undefined, configPath);
  return { program: ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options, host: parse(parsed.options) }), errors: [config.error, ...parsed.errors] };
}

function mapRepo(request, programs) {
  const repoRoot = path.resolve(request.repo_root);
  const included = new Set(request.paths);
  const mapped = new Set();
  const symbols = new Map();
  const bodyless = new Map();
  const edges = new Map();
  const entryPoints = new Map();
  const unresolvedNames = new Map();
  const diagnostics = new Set();
  const httpCalls = [];
  const uiElements = [];
  let resolved = 0;
  let unresolved = 0;
  const sourceFiles = new Map();
  const registries = new Map();
  let parsedFiles = 0;

  for (const [index, { tsconfig, ts, module }] of programs.entries()) {
    // D66: a file several tsconfigs include is parsed once. A SourceFile is reused only by the same typescript module, for the same parse
    // options, and under the compiler-options key tsserver's DocumentRegistry shares files by, so binding sees what it would have seen alone.
    const parse = (options) => {
      if (!registries.has(module)) {
        registries.set(module, typeof ts.createDocumentRegistry === 'function' ? ts.createDocumentRegistry() : undefined);
      }

      const registry = registries.get(module);
      const settings = registry === undefined ? `program ${index}` : registry.getKeyForCompilationSettings(options);
      const host = ts.createCompilerHost(options);
      const getSourceFile = host.getSourceFile;
      host.getSourceFile = (fileName, version, onError, shouldCreateNewSourceFile) => {
        const parsing = typeof version === 'object' && version !== null
          ? [version.languageVersion, version.impliedNodeFormat, version.jsDocParsingMode].join(',')
          : String(version);
        const key = [module, settings, parsing, fileName].join('\n');
        if (!shouldCreateNewSourceFile && sourceFiles.has(key)) {
          return sourceFiles.get(key);
        }

        parsedFiles++;
        const sourceFile = getSourceFile.call(host, fileName, version, onError, shouldCreateNewSourceFile);
        if (sourceFile !== undefined) {
          sourceFiles.set(key, sourceFile);
        }

        return sourceFile;
      };
      return host;
    };
    const { program, errors } = createProgram(ts, repoRoot, tsconfig, request.paths, parse);
    [...errors, ...program.getOptionsDiagnostics(), ...program.getGlobalDiagnostics(), ...program.getSyntacticDiagnostics()]
      .filter((diagnostic) => diagnostic !== undefined)
      .forEach((diagnostic) => {
        const file = diagnostic.file ? path.relative(repoRoot, diagnostic.file.fileName).split(path.sep).join('/') : tsconfig ?? 'default program';
        const location = diagnostic.file && diagnostic.start !== undefined ? `${file}:${line(diagnostic.file, diagnostic.start)}` : file;
        diagnostics.add(`${location}: TS${diagnostic.code}: ${ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')}`);
      });
    const mapper = createMapper(ts, program.getTypeChecker(), repoRoot);
    const sources = program.getSourceFiles().filter((sourceFile) => !sourceFile.isDeclarationFile && included.has(mapper.repoPath(sourceFile)));
    // Mounts are read from every included file of the program, so a router mapped here still gets the prefix a file mapped earlier mounts it under.
    sources.forEach(mapper.collectMounts);
    const files = sources.filter((sourceFile) => !mapped.has(mapper.repoPath(sourceFile)));
    let done = 0;

    for (const sourceFile of files) {
      const file = mapper.repoPath(sourceFile);
      mapped.add(file);
      done += 1;
      if (Math.floor((done * 10) / files.length) > Math.floor(((done - 1) * 10) / files.length)) {
        report(`mapped ${done}/${files.length} files`);
      }

      for (const declaration of mapper.declarations(sourceFile)) {
        if (symbols.has(declaration.id)) {
          continue;
        }

        symbols.set(declaration.id, mapper.symbol(sourceFile, file, declaration));
        const calls = mapper.callSites(declaration);
        resolved += calls.resolved;
        unresolved += calls.unresolved.length;
        calls.unresolved.forEach((name) => unresolvedNames.set(name, (unresolvedNames.get(name) || 0) + 1));
        calls.targets.forEach((to) => edges.set(`${declaration.id}\n${to}`, { from: declaration.id, to, kind: 'call' }));
        calls.http.forEach((call) => httpCalls.push({ from: declaration.id, ...call, path: file }));
      }

      mapper.bodyless(sourceFile).filter((declaration) => !bodyless.has(declaration.id)).forEach((declaration) => bodyless.set(declaration.id, declaration));

      const route = pageRoute(file);
      const pageIds = route === undefined ? [] : mapper.defaultExportIds(sourceFile);
      pageIds.forEach((id) => entryPoints.set(`${id}\n${route}`, { symbol_id: id, kind: 'page', display: route }));
      const page = pageIds.map((id) => symbols.get(id)).find((symbol) => symbol !== undefined && symbol.path === file);
      uiElements.push(...mapper.uiElements(sourceFile, file, route, page === undefined ? mapper.defaultExportLine(sourceFile) : page.range.start_line));

      mapper.routes(sourceFile).forEach(({ id, display }) => entryPoints.set(`${id}\n${display}`, { symbol_id: id, kind: 'http', display }));
    }
  }

  const map = {
    symbols: [...symbols.values()].sort((a, b) => ordinal(a.id, b.id)),
    edges: [...edges.values()].filter((edge) => symbols.has(edge.to)).sort((a, b) => ordinal(a.from, b.from) || ordinal(a.to, b.to)),
    entry_points: [...entryPoints.values()].filter((entry) => symbols.has(entry.symbol_id)).sort((a, b) => ordinal(a.symbol_id, b.symbol_id) || ordinal(a.display, b.display)),
    resolution: {
      resolved,
      unresolved,
      top_unresolved_names: [...unresolvedNames]
        .sort((a, b) => b[1] - a[1] || ordinal(a[0], b[0]))
        .slice(0, 20)
        .map(([name]) => name),
    },
    diagnostics: [...diagnostics].sort(ordinal),
    http_calls: httpCalls.sort((a, b) => ordinal(a.path, b.path) || a.line - b.line || ordinal(a.from, b.from)),
    ...(uiElements.length === 0 ? {} : { ui_elements: uiElements.sort((a, b) => ordinal(a.path, b.path) || a.line - b.line) }),
  };
  const declarations = [...bodyless.values()].filter((declaration) => !symbols.has(declaration.id)).sort((a, b) => ordinal(a.id, b.id));
  if (declarations.length > 0) {
    map.declarations = declarations.map(({ node, ...declaration }) => declaration);
  }

  return request.debug === true ? { ...map, parsed_files: parsedFiles } : map;
}

function createMapper(ts, checker, repoRoot) {
  const literalKinds = new Set(LITERAL_KINDS.map((name) => ts.SyntaxKind[name]));

  function repoPath(sourceFile) {
    return path.relative(repoRoot, sourceFile.fileName).split(path.sep).join('/');
  }

  function declarations(sourceFile) {
    const found = [];
    for (const statement of sourceFile.statements) {
      if (ts.isFunctionDeclaration(statement) && statement.body) {
        found.push(declared(statement, statement, statement, 'function', ''));
      } else if (ts.isVariableStatement(statement)) {
        statement.declarationList.declarations
          .filter(isFunctionConst)
          .forEach((variable) => found.push(declared(variable, statement, variable.initializer, 'function', '')));
      } else if (ts.isClassDeclaration(statement)) {
        const brace = statement.getChildren(sourceFile).find((child) => child.kind === ts.SyntaxKind.OpenBraceToken);
        const header = collapse(sourceFile.text.slice(statement.getStart(sourceFile), brace.getStart(sourceFile))) + '\n';
        statement.members
          .filter((member) => (ts.isMethodDeclaration(member) || ts.isConstructorDeclaration(member)) && member.body)
          .forEach((member) => found.push(declared(member, member, member, ts.isConstructorDeclaration(member) ? 'constructor' : 'method', header)));
      } else if (ts.isExportAssignment(statement) && isFunctionExpression(statement.expression)) {
        found.push(declared(statement, statement, statement.expression, 'function', ''));
      } else {
        const route = routeCall(statement);
        if (route !== undefined && route.path !== undefined && isFunctionExpression(route.handler)) {
          found.push({ id: `${repoPath(sourceFile)}#${route.method} ${route.path}`, node: statement, fn: route.handler, kind: 'function', header: '' });
        }
      }
    }

    return found;
  }

  // D72: top-level declarations without a body (types, enums and their members, class fields, plain variables) that references can point at.
  function bodyless(sourceFile) {
    const file = repoPath(sourceFile);
    const found = [];
    const text = (from, to) => collapse(sourceFile.text.slice(from.getStart(sourceFile), to));
    const add = (node, name, kind, signature, hashed = node) => found.push({
      id: `${file}#${name}`,
      path: file,
      range: { start_line: line(sourceFile, node.getStart(sourceFile)), end_line: line(sourceFile, node.end) },
      kind,
      signature,
      body_hash: crypto.createHash('sha256').update(tokenText(hashed, sourceFile), 'utf8').digest('hex'),
      node,
    });
    // A field or parameter without its initializer: modifiers, name, optional or definite marker and type.
    const member = (node) => text(node, (node.type || node.exclamationToken || node.questionToken || node.name).end);
    const until = (node, kind) => text(node, node.getChildren(sourceFile).find((child) => child.kind === kind).getStart(sourceFile));
    const header = (node) => until(node, ts.SyntaxKind.OpenBraceToken);

    for (const statement of sourceFile.statements) {
      if (ts.isClassDeclaration(statement)) {
        const name = className(statement);
        const head = header(statement);
        add(statement, name, 'class', head);
        for (const field of statement.members) {
          if (ts.isPropertyDeclaration(field)) {
            add(field, `${name}.${field.name.getText()}`, 'field', `${head}\n${member(field)}`);
          } else if (ts.isConstructorDeclaration(field)) {
            field.parameters
              .filter((parameter) => ts.isParameterPropertyDeclaration(parameter, field))
              .forEach((parameter) => add(parameter, `${name}.${parameter.name.getText()}`, 'field', `${head}\n${member(parameter)}`));
          }
        }
      } else if (ts.isInterfaceDeclaration(statement)) {
        add(statement, statement.name.text, 'interface', header(statement));
      } else if (ts.isTypeAliasDeclaration(statement)) {
        add(statement, statement.name.text, 'type', until(statement, ts.SyntaxKind.EqualsToken));
      } else if (ts.isEnumDeclaration(statement)) {
        const head = header(statement);
        add(statement, statement.name.text, 'enum', head);
        statement.members.forEach((value) => add(value, `${statement.name.text}.${value.name.getText()}`, 'enum_member', `${head}\n${value.name.getText()}`));
      } else if (ts.isVariableStatement(statement)) {
        const list = statement.declarationList;
        const keyword = collapse(sourceFile.text.slice(statement.getStart(sourceFile), list.declarations[0].getStart(sourceFile)));
        const kind = (list.flags & ts.NodeFlags.Const) !== 0 ? 'constant' : 'variable';
        list.declarations
          .filter((variable) => ts.isIdentifier(variable.name) && !isFunctionConst(variable) && !isAlias(variable))
          .forEach((variable) => {
            const single = list.declarations.length === 1;
            add(single ? statement : variable, variable.name.text, kind, `${keyword} ${member(variable)}`, single ? statement : variable);
          });
      }
    }

    return found;
  }

  // A JavaScript `const x = require('y')` is an import the checker aliases, not a constant.
  function isAlias(variable) {
    const symbol = checker.getSymbolAtLocation(variable.name);
    return symbol !== undefined && (symbol.flags & ts.SymbolFlags.Alias) !== 0;
  }

  const mounts = new Map();

  function collectMounts(sourceFile) {
    for (const statement of sourceFile.statements) {
      const route = routeCall(statement);
      const child = route !== undefined && route.verb === 'use' ? mountedServer(route.handler) : undefined;
      if (child !== undefined) {
        mounts.set(child, [...(mounts.get(child) || []), { parent: route.server, prefix: route.path ?? '' }]);
      }
    }
  }

  // An http entry point per route registered at the top level of the file, once per prefix its router is mounted under (D64).
  function routes(sourceFile) {
    const found = [];
    for (const statement of sourceFile.statements) {
      const route = routeCall(statement);
      if (route === undefined || route.path === undefined || (route.verb === 'use' && mountedServer(route.handler) !== undefined)) {
        continue;
      }

      const ids = isFunctionExpression(route.handler)
        ? [`${repoPath(sourceFile)}#${route.method} ${route.path}`]
        : ts.isIdentifier(route.handler) || ts.isPropertyAccessExpression(route.handler)
          ? targetIds(checker.getSymbolAtLocation(ts.isPropertyAccessExpression(route.handler) ? route.handler.name : route.handler))
          : [];
      prefixes(route.server, new Set()).forEach((prefix) => ids.forEach((id) => found.push({ id, display: `${route.method} ${routeTemplate(prefix, route.path)}` })));
    }

    return found;
  }

  function prefixes(server, seen) {
    const parents = mounts.get(server) || [];
    if (parents.length === 0 || seen.has(server) || seen.size >= 8) {
      return [''];
    }

    const inner = new Set([...seen, server]);
    return [...new Set(parents.flatMap(({ parent, prefix }) => prefixes(parent, inner).map((outer) => `${outer}/${prefix}`)))];
  }

  // `app.get("/x", ...handlers)` as a top-level statement on an Express app or router, a Koa router or a Fastify instance: the path is the first
  // literal starting with / among the first two arguments (a Koa route may be named first) and the handler is the last argument.
  function routeCall(statement) {
    const call = ts.isExpressionStatement(statement) ? unwrap(statement.expression) : undefined;
    if (call === undefined || !ts.isCallExpression(call) || !ts.isPropertyAccessExpression(call.expression) || !ts.isIdentifier(call.expression.name)
      || !ROUTE_VERBS.has(call.expression.name.text) || call.arguments.length === 0) {
      return undefined;
    }

    const server = serverDeclaration(call.expression.expression);
    if (server === undefined) {
      return undefined;
    }

    const verb = call.expression.name.text;
    const path = call.arguments.slice(0, 2).map(literalText).find((text) => text !== undefined && text.startsWith('/'));
    const method = verb === 'all' || verb === 'use' ? 'ANY' : verb === 'del' ? 'DELETE' : verb.toUpperCase();
    return { server, verb, method, path, handler: unwrap(call.arguments[call.arguments.length - 1]) };
  }

  function literalText(node) {
    const value = unwrap(node);
    return ts.isStringLiteral(value) || ts.isNoSubstitutionTemplateLiteral(value) ? value.text : undefined;
  }

  // A router passed to use(), directly or as Koa's router.routes().
  function mountedServer(handler) {
    const target = ts.isCallExpression(handler) && ts.isPropertyAccessExpression(handler.expression) && handler.expression.name.text === 'routes'
      ? handler.expression.expression
      : handler;
    return serverDeclaration(target);
  }

  function serverDeclaration(expression) {
    const node = unwrap(expression);
    const symbol = ts.isIdentifier(node) ? aliased(checker.getSymbolAtLocation(node)) : undefined;
    const declaration = symbol && symbol.declarations && symbol.declarations[0];
    return declaration && ts.isVariableDeclaration(declaration) && declaration.initializer !== undefined
      && ts.isVariableStatement(declaration.parent.parent) && ts.isSourceFile(declaration.parent.parent.parent) && createsServer(declaration.initializer)
      ? declaration
      : undefined;
  }

  function createsServer(initializer) {
    const node = unwrap(initializer);
    if (!ts.isCallExpression(node) && !ts.isNewExpression(node)) {
      return false;
    }

    const callee = unwrap(node.expression);
    if (ts.isIdentifier(callee)) {
      const imported = importOf(callee);
      return imported !== undefined && (SERVER_FACTORIES[imported.module] || []).includes(imported.name);
    }

    if (ts.isPropertyAccessExpression(callee) && callee.name.text === 'Router') {
      const owner = unwrap(callee.expression);
      return (ts.isIdentifier(owner) ? (importOf(owner) || {}).module : requireOf(owner)) === 'express';
    }

    return ts.isCallExpression(callee) && Object.hasOwn(SERVER_FACTORIES, requireOf(callee) ?? '');
  }

  // The module and export an identifier is imported or required as: 'default' for a default, namespace or whole-module import.
  function importOf(identifier) {
    const symbol = checker.getSymbolAtLocation(identifier);
    const declaration = symbol && symbol.declarations && symbol.declarations[0];
    const from = (specifier, name) => (specifier !== undefined && ts.isStringLiteral(specifier) ? { module: specifier.text, name } : undefined);
    if (declaration === undefined) {
      return undefined;
    }

    if (ts.isImportClause(declaration)) {
      return from(declaration.parent.moduleSpecifier, 'default');
    }

    if (ts.isNamespaceImport(declaration)) {
      return from(declaration.parent.parent.moduleSpecifier, 'default');
    }

    if (ts.isImportSpecifier(declaration)) {
      return from(declaration.parent.parent.parent.moduleSpecifier, (declaration.propertyName || declaration.name).text);
    }

    if (ts.isVariableDeclaration(declaration) && declaration.initializer !== undefined && requireOf(declaration.initializer) !== undefined) {
      return { module: requireOf(declaration.initializer), name: 'default' };
    }

    if (ts.isBindingElement(declaration) && ts.isObjectBindingPattern(declaration.parent) && declaration.parent.parent.initializer !== undefined) {
      const module = requireOf(declaration.parent.parent.initializer);
      return module === undefined ? undefined : { module, name: (declaration.propertyName || declaration.name).getText() };
    }

    return undefined;
  }

  function requireOf(expression) {
    const node = unwrap(expression);
    return ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'require' && node.arguments.length === 1
      ? literalText(node.arguments[0])
      : undefined;
  }

  function declared(named, node, fn, kind, header) {
    return { id: idFor(named), node, fn, kind, header };
  }

  function symbol(sourceFile, file, declaration) {
    const start = declaration.node.getStart(sourceFile);
    return {
      id: declaration.id,
      path: file,
      range: { start_line: line(sourceFile, start), end_line: line(sourceFile, declaration.node.end) },
      kind: declaration.kind,
      signature: declaration.header + collapse(sourceFile.text.slice(start, declaration.fn.body.getStart(sourceFile))),
      body_hash: crypto.createHash('sha256').update(tokenText(declaration.node, sourceFile), 'utf8').digest('hex'),
      normalized_hash: crypto.createHash('sha256').update(normalizedTokens(declaration.fn.body, sourceFile, []).join(' '), 'utf8').digest('hex'),
    };
  }

  // D68: the body's tokens with every identifier and every literal replaced by a placeholder, so copies that differ only in names and literals
  // match; keywords, operators, punctuation and the shape of property accesses stay, and trivia (whitespace, comments, JSDoc) never appears.
  function normalizedTokens(node, sourceFile, tokens) {
    if (ts.isJSDoc(node)) {
      return tokens;
    }

    const children = node.getChildren(sourceFile);
    if (children.length > 0) {
      children.forEach((child) => normalizedTokens(child, sourceFile, tokens));
    } else if (node.kind === ts.SyntaxKind.Identifier || node.kind === ts.SyntaxKind.PrivateIdentifier) {
      tokens.push('$id');
    } else if (node.kind === ts.SyntaxKind.JsxText) {
      if (collapse(node.text) !== '') {
        tokens.push('$literal');
      }
    } else if (literalKinds.has(node.kind)) {
      tokens.push('$literal');
    } else if (node.end > node.pos) {
      tokens.push(node.getText(sourceFile));
    }

    return tokens;
  }

  // D69: the file's UI structure, read from its markup in document order. A heading applies to what follows it until a heading of the same or a
  // higher level; a fieldset, a section or a titled Section/Card/Panel/Group component scopes the headings inside it.
  function uiElements(sourceFile, file, route, routeLine) {
    const found = [];
    const element = (kind, text, at, extra) => {
      const entry = { kind, text, path: file, line: typeof at === 'number' ? at : line(sourceFile, at.getStart(sourceFile)) };
      Object.entries({ ...extra, route }).filter(([, value]) => value !== undefined).forEach(([key, value]) => { entry[key] = value; });
      found.push(entry);
    };
    if (route !== undefined) {
      element('route', route, routeLine, {});
    }

    const labels = new Map();
    const collectLabels = (node) => {
      if (ts.isJsxElement(node) && tagOf(node.openingElement) === 'label' && attribute(node.openingElement, 'htmlFor') !== undefined) {
        labels.set(attribute(node.openingElement, 'htmlFor'), jsxText(node));
      }

      ts.forEachChild(node, collectLabels);
    };
    collectLabels(sourceFile);

    for (const statement of sourceFile.statements) {
      const headings = [];
      const section = () => (headings.length === 0 ? undefined : headings[headings.length - 1].text);
      const heading = (level, text, at) => {
        while (headings.length > 0 && headings[headings.length - 1].level >= level) {
          headings.pop();
        }

        element('heading', text, at, { section: section() });
        headings.push({ level, text });
      };
      const walk = (node) => {
        if (ts.isArrayLiteralExpression(node)) {
          node.elements.filter(ts.isObjectLiteralExpression).forEach((item) => {
            const text = NAV_TEXT.map((name) => literalProperty(item, name)).find((value) => value !== undefined);
            const target = NAV_TARGET.map((name) => literalProperty(item, name)).find((value) => value !== undefined && /^(\/|https?:)/.test(value));
            if (text !== undefined && target !== undefined) {
              element('nav', text, item, { target, section: section() });
            }
          });
        }

        const opening = ts.isJsxElement(node) ? node.openingElement : ts.isJsxSelfClosingElement(node) ? node : undefined;
        if (opening === undefined) {
          ts.forEachChild(node, walk);
          return;
        }

        const tag = tagOf(opening);
        const scoped = SCOPES.has(tag) || (!/^[a-z]/.test(tag) && /(Section|Card|Panel|Group|Fieldset)$/.test(tag));
        const saved = scoped ? [...headings] : undefined;
        const text = ts.isJsxElement(node) ? jsxText(node) : '';
        const control = controlOf(tag, opening);
        const target = attribute(opening, 'href', true) ?? attribute(opening, 'to', true);
        if (/^h[1-3]$/.test(tag) && text !== '') {
          heading(Number(tag[1]), text, opening);
        } else if (tag === 'legend' && text !== '') {
          heading(3, text, opening);
        } else if (control !== undefined) {
          element('control', labelOf(node, opening, labels), opening, { control, section: section() });
        } else if (target !== undefined && (/NavLink$/.test(tag) || insideNavigation(node))) {
          element('nav', text || attribute(opening, 'aria-label') || attribute(opening, 'title') || target, opening, { target, section: section() });
        } else if (scoped && tag !== 'fieldset') {
          const title = ['title', 'heading', 'label', 'aria-label'].map((name) => attribute(opening, name)).find((value) => value !== undefined && value !== '');
          if (title !== undefined) {
            heading(2, title, opening);
          }
        }

        ts.forEachChild(node, walk);
        if (saved !== undefined) {
          headings.splice(0, headings.length, ...saved);
        }
      };
      walk(statement);
    }

    return found.sort((a, b) => a.line - b.line).slice(0, UI_ELEMENTS_PER_FILE);
  }

  function tagOf(opening) {
    return opening.tagName.getText();
  }

  // A static attribute value; with fold, an href built from literals and constants reads as a route template.
  function attribute(opening, name, folded) {
    const found = opening.attributes.properties.find((property) => ts.isJsxAttribute(property) && property.name.getText() === name);
    const value = found && found.initializer;
    if (value === undefined) {
      return undefined;
    }

    if (ts.isStringLiteral(value)) {
      return value.text;
    }

    return ts.isJsxExpression(value) && value.expression !== undefined ? (folded ? fold(value.expression, 0) : literalText(value.expression)) : undefined;
  }

  function jsxText(node, excluded) {
    const parts = [];
    const visit = (child) => {
      if (child === excluded) {
        return;
      }

      if (child.kind === ts.SyntaxKind.JsxText) {
        parts.push(child.text);
      } else if (ts.isJsxExpression(child) && child.expression !== undefined && literalText(child.expression) !== undefined) {
        parts.push(literalText(child.expression));
      } else if (ts.isJsxElement(child) || ts.isJsxFragment(child)) {
        child.children.forEach(visit);
      }
    };
    node.children.forEach(visit);
    return collapse(parts.join(' '));
  }

  function controlOf(tag, opening) {
    if (tag === 'input') {
      const type = (attribute(opening, 'type') ?? 'text').toLowerCase();
      return ['hidden', 'submit', 'button', 'reset', 'image'].includes(type) ? undefined : type;
    }

    if (tag === 'select' || tag === 'textarea') {
      return tag;
    }

    return !/^[a-z]/.test(tag) && CONTROL_COMPONENT.test(tag) ? tag : undefined;
  }

  // The text of an enclosing <label>, else of the <label htmlFor> naming its id, else its own label, aria-label, title, placeholder or name.
  function labelOf(node, opening, labels) {
    for (let parent = node.parent; parent !== undefined && !ts.isSourceFile(parent); parent = parent.parent) {
      if (ts.isJsxElement(parent) && tagOf(parent.openingElement) === 'label') {
        const text = jsxText(parent, node);
        if (text !== '') {
          return text;
        }
      }
    }

    const id = attribute(opening, 'id');
    if (id !== undefined && labels.has(id) && labels.get(id) !== '') {
      return labels.get(id);
    }

    return ['label', 'aria-label', 'title', 'placeholder', 'name'].map((name) => attribute(opening, name)).find((value) => value !== undefined && value !== '') ?? '';
  }

  function insideNavigation(node) {
    for (let parent = node.parent; parent !== undefined && !ts.isSourceFile(parent); parent = parent.parent) {
      const opening = ts.isJsxElement(parent) ? parent.openingElement : undefined;
      if (opening === undefined) {
        continue;
      }

      const tag = tagOf(opening);
      if (NAV_CONTAINERS.has(tag) || (!/^[a-z]/.test(tag) && /(Nav|Menu|Sidebar|Tabs|Breadcrumb)/i.test(tag))
        || ['navigation', 'menu', 'menubar', 'tablist'].includes(attribute(opening, 'role'))
        || /(^|[\s_-])(nav|navbar|menu|sidebar)([\s_-]|$)/i.test(attribute(opening, 'className') ?? '')) {
        return true;
      }
    }

    return false;
  }

  function literalProperty(object, name) {
    const value = property(object, name);
    return value === undefined ? undefined : literalText(value);
  }

  function callSites(declaration) {
    const calls = { resolved: 0, unresolved: [], targets: new Set(), http: [] };
    const site = (callee) => {
      const name = ts.isPropertyAccessExpression(callee) ? callee.name : callee;
      const target = aliased(checker.getSymbolAtLocation(name));
      if (target && target.declarations && target.declarations.length > 0) {
        calls.resolved++;
        targetIds(target).forEach((id) => calls.targets.add(id));
      } else {
        calls.unresolved.push(ts.isIdentifier(name) || ts.isPrivateIdentifier(name) ? name.text : collapse(name.getText()));
      }
    };
    const visit = (node) => {
      if (ts.isCallExpression(node)) {
        site(node.expression);
        const http = httpCall(node);
        if (http !== undefined) {
          calls.http.push(http);
        }

        node.arguments
          .filter((argument) => ts.isIdentifier(argument))
          .forEach((argument) => targetIds(checker.getSymbolAtLocation(argument)).forEach((id) => calls.targets.add(id)));
      } else if (ts.isNewExpression(node)) {
        site(node.expression);
      } else if ((ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) && !isIntrinsic(node.tagName)) {
        site(node.tagName);
      }

      ts.forEachChild(node, visit);
    };
    visit(declaration.fn);
    return calls;
  }

  // D61: only what the call's own text says is recorded; a URL that cannot be folded from literals and constants is kept unresolved, never guessed.
  function httpCall(node) {
    const callee = node.expression;
    const [first, second] = node.arguments;
    if (first === undefined) {
      return undefined;
    }

    if (isFetch(callee)) {
      return request(node, first, second === undefined ? 'GET' : methodOf(second), '');
    }

    if (ts.isPropertyAccessExpression(callee) && ts.isIdentifier(callee.name)) {
      const base = clientBase(callee.expression);
      const verb = callee.name.text;
      if (base !== undefined && AXIOS_VERBS.has(verb)) {
        return request(node, first, verb.toUpperCase(), base);
      }

      if (base !== undefined && verb === 'request') {
        return configured(node, first, base);
      }
    }

    const base = clientBase(callee);
    if (base === undefined) {
      return undefined;
    }

    return ts.isObjectLiteralExpression(unwrap(first)) || (second === undefined && fold(first, 0) === undefined)
      ? configured(node, first, base)
      : request(node, first, second === undefined ? 'GET' : methodOf(second), base);
  }

  function request(node, urlExpression, method, base) {
    const raw = fold(urlExpression, 0);
    return {
      method,
      url: raw === undefined ? null : normalizeUrl(raw, base),
      text: collapse(urlExpression.getText()),
      line: line(node.getSourceFile(), node.getStart()),
    };
  }

  function configured(node, config, base) {
    const object = unwrap(config);
    if (!ts.isObjectLiteralExpression(object)) {
      return { method: 'ANY', url: null, text: collapse(config.getText()), line: line(node.getSourceFile(), node.getStart()) };
    }

    const baseUrl = property(object, 'baseURL');
    const url = property(object, 'url');
    const call = request(node, url === undefined ? object : url, methodOf(object), baseUrl === undefined ? base : fold(baseUrl, 0) ?? parameter(baseUrl));
    return url === undefined ? { ...call, url: null } : call;
  }

  function methodOf(init) {
    const object = unwrap(init);
    if (!ts.isObjectLiteralExpression(object)) {
      return 'ANY';
    }

    const method = property(object, 'method');
    if (method === undefined) {
      return object.properties.some((member) => ts.isSpreadAssignment(member)) ? 'ANY' : 'GET';
    }

    const value = unwrap(method);
    return ts.isStringLiteral(value) || ts.isNoSubstitutionTemplateLiteral(value) ? value.text.toUpperCase() : 'ANY';
  }

  function property(object, name) {
    for (const member of object.properties) {
      const key = member.name && (ts.isIdentifier(member.name) || ts.isStringLiteral(member.name)) ? member.name.text : undefined;
      if (key !== name) {
        continue;
      }

      if (ts.isPropertyAssignment(member)) {
        return member.initializer;
      }

      if (ts.isShorthandPropertyAssignment(member)) {
        return member.name;
      }
    }

    return undefined;
  }

  // Folds string literals, templates, + concatenations and const initializers into a path; each runtime part becomes a {name} parameter.
  function fold(expression, depth) {
    const node = unwrap(expression);
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      return node.text;
    }

    if (ts.isTemplateExpression(node)) {
      return node.head.text + node.templateSpans.map((span) => (fold(span.expression, depth) ?? parameter(span.expression)) + span.literal.text).join('');
    }

    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.PlusToken) {
      const left = fold(node.left, depth);
      const right = fold(node.right, depth);
      return left === undefined && right === undefined ? undefined : (left ?? parameter(node.left)) + (right ?? parameter(node.right));
    }

    if (ts.isIdentifier(node) && depth < 5) {
      const declaration = constDeclaration(aliased(checker.getSymbolAtLocation(node)));
      return declaration === undefined ? undefined : fold(declaration.initializer, depth + 1);
    }

    return undefined;
  }

  function parameter(expression) {
    const text = collapse(expression.getText());
    return /^[A-Za-z_$][\w$]*(\.[A-Za-z_$][\w$]*)*$/.test(text) ? `{${text}}` : '{param}';
  }

  function constDeclaration(symbol) {
    const declaration = symbol && symbol.declarations && symbol.declarations[0];
    return declaration && ts.isVariableDeclaration(declaration) && declaration.initializer !== undefined
      && ts.isVariableDeclarationList(declaration.parent) && (declaration.parent.flags & ts.NodeFlags.Const) !== 0
      ? declaration
      : undefined;
  }

  function isFetch(callee) {
    const name = ts.isIdentifier(callee)
      ? callee
      : ts.isPropertyAccessExpression(callee) && ts.isIdentifier(callee.expression) && ['window', 'globalThis', 'self'].includes(callee.expression.text)
        ? callee.name
        : undefined;
    return name !== undefined && name.text === 'fetch' && isExternal(aliased(checker.getSymbolAtLocation(name)));
  }

  // The base path an axios client puts before every relative URL: '' for axios itself, an axios.create instance's baseURL, undefined for anything else.
  function clientBase(expression) {
    const node = unwrap(expression);
    if (!ts.isIdentifier(node)) {
      return undefined;
    }

    if (isAxios(node)) {
      return '';
    }

    const declaration = constDeclaration(aliased(checker.getSymbolAtLocation(node)));
    const create = declaration && unwrap(declaration.initializer);
    if (create === undefined || !ts.isCallExpression(create) || !ts.isPropertyAccessExpression(create.expression)
      || create.expression.name.text !== 'create' || !ts.isIdentifier(create.expression.expression) || !isAxios(create.expression.expression)) {
      return undefined;
    }

    const config = create.arguments[0] && unwrap(create.arguments[0]);
    const baseUrl = config && ts.isObjectLiteralExpression(config) ? property(config, 'baseURL') : undefined;
    return baseUrl === undefined ? '' : fold(baseUrl, 0) ?? parameter(baseUrl);
  }

  function isAxios(identifier) {
    const symbol = checker.getSymbolAtLocation(identifier);
    if (symbol === undefined) {
      return identifier.text === 'axios';
    }

    return (symbol.declarations || []).some((declaration) => moduleOf(declaration) === 'axios');
  }

  function moduleOf(declaration) {
    const specifier = ts.isImportClause(declaration) ? declaration.parent.moduleSpecifier
      : ts.isNamespaceImport(declaration) ? declaration.parent.parent.moduleSpecifier
        : ts.isImportSpecifier(declaration) && (declaration.propertyName || declaration.name).text === 'default' ? declaration.parent.parent.parent.moduleSpecifier
          : isRequire(declaration) ? declaration.initializer.arguments[0]
            : undefined;
    return specifier !== undefined && ts.isStringLiteral(specifier) ? specifier.text : undefined;
  }

  function isRequire(declaration) {
    return ts.isVariableDeclaration(declaration) && declaration.initializer !== undefined && ts.isCallExpression(declaration.initializer)
      && ts.isIdentifier(declaration.initializer.expression) && declaration.initializer.expression.text === 'require';
  }

  function isExternal(symbol) {
    return !symbol || !symbol.declarations || symbol.declarations.every((declaration) => declaration.getSourceFile().isDeclarationFile);
  }

  function unwrap(node) {
    let current = node;
    while (ts.isParenthesizedExpression(current) || ts.isAsExpression(current) || ts.isNonNullExpression(current)
      || (typeof ts.isSatisfiesExpression === 'function' && ts.isSatisfiesExpression(current))) {
      current = current.expression;
    }

    return current;
  }

  // The line of the file's export default statement, or 1 when it has none.
  function defaultExportLine(sourceFile) {
    const statement = sourceFile.statements.find((node) => ts.isExportAssignment(node)
      || ((ts.getCombinedModifierFlags(node) & ts.ModifierFlags.ExportDefault) === ts.ModifierFlags.ExportDefault));
    return statement === undefined ? 1 : line(sourceFile, statement.getStart(sourceFile));
  }

  function defaultExportIds(sourceFile) {
    const moduleSymbol = checker.getSymbolAtLocation(sourceFile);
    return targetIds(moduleSymbol && checker.getExportsOfModule(moduleSymbol).find((exported) => exported.name === 'default'));
  }

  function targetIds(symbol) {
    const target = aliased(symbol);
    return [...new Set(((target && target.declarations) || []).map(idFor).filter((id) => id !== undefined))];
  }

  function idFor(node) {
    const parent = node.parent;
    const file = () => repoPath(node.getSourceFile());
    if (ts.isFunctionDeclaration(node) && ts.isSourceFile(parent)) {
      return `${file()}#${node.name ? node.name.text : 'default'}`;
    }

    if (ts.isVariableDeclaration(node) && isFunctionConst(node) && ts.isVariableStatement(parent.parent) && ts.isSourceFile(parent.parent.parent)) {
      return `${file()}#${node.name.text}`;
    }

    if (ts.isExportAssignment(node) && ts.isSourceFile(parent) && isFunctionExpression(node.expression)) {
      return `${file()}#default`;
    }

    if (ts.isClassDeclaration(node) && ts.isSourceFile(parent)) {
      return `${file()}#${className(node)}.constructor`;
    }

    if ((ts.isMethodDeclaration(node) || ts.isConstructorDeclaration(node)) && ts.isClassDeclaration(parent) && ts.isSourceFile(parent.parent)) {
      const isStatic = (member) => (ts.getCombinedModifierFlags(member) & ts.ModifierFlags.Static) !== 0;
      const collision = ts.isMethodDeclaration(node) && isStatic(node) && parent.members.some((member) =>
        ts.isMethodDeclaration(member) && !isStatic(member) && member.name.getText() === node.name.getText());
      return `${file()}#${className(parent)}.${collision ? 'static.' : ''}${ts.isConstructorDeclaration(node) ? 'constructor' : node.name.getText()}`;
    }

    return undefined;
  }

  function aliased(symbol) {
    return symbol && symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol;
  }

  function isFunctionConst(variable) {
    return ts.isIdentifier(variable.name)
      && variable.initializer !== undefined
      && isFunctionExpression(variable.initializer)
      && (variable.parent.flags & ts.NodeFlags.Const) !== 0;
  }

  function isFunctionExpression(node) {
    return ts.isArrowFunction(node) || ts.isFunctionExpression(node);
  }

  function isIntrinsic(tagName) {
    return tagName.kind === ts.SyntaxKind.JsxNamespacedName
      || (ts.isIdentifier(tagName) && (/^[a-z]/.test(tagName.text) || tagName.text.includes('-')));
  }

  function className(node) {
    return node.name ? node.name.text : 'default';
  }

  function tokenText(node, sourceFile) {
    const children = node.getChildren(sourceFile);
    if (children.length === 0) {
      return node.kind === ts.SyntaxKind.JsxText ? collapse(node.text) : node.getText(sourceFile);
    }

    return children
      .filter((child) => !ts.isJSDoc(child))
      .map((child) => tokenText(child, sourceFile))
      .join('');
  }

  return { repoPath, declarations, bodyless, symbol, callSites, defaultExportIds, collectMounts, routes, uiElements, defaultExportLine };
}

const AXIOS_VERBS = new Set(['get', 'post', 'put', 'patch', 'delete', 'head', 'options']);

const LITERAL_KINDS = [
  'StringLiteral', 'NumericLiteral', 'BigIntLiteral', 'RegularExpressionLiteral', 'NoSubstitutionTemplateLiteral', 'TemplateHead', 'TemplateMiddle', 'TemplateTail',
];

const UI_ELEMENTS_PER_FILE = 200;
const SCOPES = new Set(['fieldset', 'section']);
const NAV_CONTAINERS = new Set(['nav', 'aside', 'header', 'menu']);
const NAV_TEXT = ['label', 'title', 'name', 'text'];
const NAV_TARGET = ['href', 'to', 'path', 'url', 'link'];
const CONTROL_COMPONENT = /(Checkbox|Switch|Toggle|Select|Input|TextField|TextArea|Textarea|Radio|RadioGroup|Slider|Combobox|DatePicker)$/;

const ROUTE_VERBS = new Set(['get', 'post', 'put', 'patch', 'delete', 'del', 'all', 'use', 'head', 'options']);

// The exports that create an app or router whose verb methods register routes.
const SERVER_FACTORIES = { express: ['default', 'Router'], fastify: ['default', 'fastify'], '@koa/router': ['default'], 'koa-router': ['default'] };

// Joins a mount prefix and a route path, writing Express parameters as the braces the UI-to-API join reads (D61): :id is {id}, :id? is {id?}, *rest is {*rest}.
function routeTemplate(prefix, route) {
  const segments = `${prefix}/${route}`.split('/').filter((segment) => segment !== '').map((segment) => {
    const parameter = /^:([A-Za-z_$][\w$]*)(\(.*\))?(\?)?$/.exec(segment);
    if (parameter !== null) {
      return `{${parameter[1]}${parameter[3] ?? ''}}`;
    }

    return segment.startsWith('*') ? `{${segment}}` : segment;
  });
  return '/' + segments.join('/');
}

// Drops the query string, fragment and origin; a relative URL gets the client's base path in front, as axios does.
function normalizeUrl(raw, base) {
  const url = stripUrl(raw);
  if (url.absolute || base === '') {
    return url.path;
  }

  return `${stripUrl(base).path.replace(/\/+$/, '')}/${url.path.replace(/^\/+/, '')}`;
}

function stripUrl(url) {
  const path = url.split(/[?#]/)[0];
  const origin = /^([a-z][a-z0-9+.-]*:)?\/\/[^/]*/i.exec(path);
  return origin === null ? { path, absolute: false } : { path: path.slice(origin[0].length) || '/', absolute: true };
}

function pageRoute(file) {
  const segments = file.split('/');
  const name = segments[segments.length - 1];
  const script = /\.(tsx|ts|jsx|js)$/;
  const app = segments.indexOf('app');
  if (app >= 0 && /^page\.(tsx|ts|jsx|js)$/.test(name)) {
    return '/' + segments.slice(app + 1, -1).filter((segment) => !/^\(.*\)$/.test(segment)).join('/');
  }

  const pages = segments.indexOf('pages');
  if (pages < 0 || !script.test(name) || segments[pages + 1] === 'api') {
    return undefined;
  }

  const route = [...segments.slice(pages + 1, -1), name.replace(script, '')];
  const last = route[route.length - 1];
  if (['_app', '_document', '_error'].includes(last)) {
    return undefined;
  }

  return '/' + (last === 'index' ? route.slice(0, -1) : route).join('/');
}

function line(sourceFile, position) {
  return sourceFile.getLineAndCharacterOfPosition(position).line + 1;
}

function collapse(text) {
  return text.replace(/\s+/g, ' ').trim();
}

function ordinal(a, b) {
  return a < b ? -1 : a > b ? 1 : 0;
}
