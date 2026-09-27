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
    const compilers = new Map();
    for (const tsconfig of request.tsconfigs) {
      const loaded = loadTypeScript(request.repo_root, tsconfig);
      if (loaded.error !== undefined) {
        fail(loaded.error);
        return;
      }

      compilers.set(tsconfig, loaded.ts);
    }

    process.stdout.write(JSON.stringify(mapRepo(request, compilers)));
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
function loadTypeScript(repoRoot, tsconfig) {
  const folder = path.posix.dirname(tsconfig);
  const directory = path.dirname(path.resolve(repoRoot, tsconfig));
  // npm's --prefix on a workspace member writes a second lockfile there, so npm's command is named only where the folder has its own.
  const hint = (npm, other) => (fs.existsSync(path.join(directory, 'package-lock.json')) ? `run ${npm} --prefix ${folder}` : other);
  const candidates = ['typescript', '@typescript/typescript6']
    .map((name) => ({ name, file: resolvePackage(name, [directory]) }))
    .filter((candidate) => candidate.file !== undefined);
  if (candidates.length === 0) {
    return { error: `typescript was not found for ${tsconfig}; ${hint('npm ci', `install the dependencies of ${folder} with its package manager`)}` };
  }

  let version;
  for (const candidate of candidates) {
    let ts;
    try {
      ts = require(candidate.file);
    } catch (error) {
      return { error: `${tsconfig}: ${candidate.name} could not be loaded (${firstLine(error)}); ${hint('npm ci', `reinstall the dependencies of ${folder} with its package manager`)}` };
    }

    if (typeof ts.createProgram === 'function') {
      return { ts };
    }

    version ??= ts.version;
  }

  return {
    error: `${tsconfig}: typescript ${version} has no JavaScript compiler API; `
      + hint('npm i -D @typescript/typescript6', `add @typescript/typescript6 as a dev dependency of ${folder} with its package manager`),
  };
}

function resolvePackage(name, paths) {
  try {
    return require.resolve(name, { paths });
  } catch {
    return undefined;
  }
}

function mapRepo(request, compilers) {
  const repoRoot = path.resolve(request.repo_root);
  const included = new Set(request.paths);
  const mapped = new Set();
  const symbols = new Map();
  const edges = new Map();
  const entryPoints = new Map();
  const unresolvedNames = new Map();
  const diagnostics = new Set();
  const httpCalls = [];
  let resolved = 0;
  let unresolved = 0;

  for (const tsconfig of request.tsconfigs) {
    report(`loading ${tsconfig}`);
    const configPath = path.join(repoRoot, tsconfig);
    const ts = compilers.get(tsconfig);
    const config = ts.readConfigFile(configPath, ts.sys.readFile);
    const parsed = ts.parseJsonConfigFileContent(config.config || {}, ts.sys, path.dirname(configPath), undefined, configPath);
    const program = ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options });
    [config.error, ...parsed.errors, ...program.getOptionsDiagnostics(), ...program.getGlobalDiagnostics(), ...program.getSyntacticDiagnostics()]
      .filter((diagnostic) => diagnostic !== undefined)
      .forEach((diagnostic) => {
        const file = diagnostic.file ? path.relative(repoRoot, diagnostic.file.fileName).split(path.sep).join('/') : tsconfig;
        const location = diagnostic.file && diagnostic.start !== undefined ? `${file}:${line(diagnostic.file, diagnostic.start)}` : file;
        diagnostics.add(`${location}: TS${diagnostic.code}: ${ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')}`);
      });
    const mapper = createMapper(ts, program.getTypeChecker(), repoRoot);
    const files = program.getSourceFiles().filter((sourceFile) => {
      const file = mapper.repoPath(sourceFile);
      return !sourceFile.isDeclarationFile && included.has(file) && !mapped.has(file);
    });
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

      const route = pageRoute(file);
      if (route !== undefined) {
        mapper.defaultExportIds(sourceFile).forEach((id) => entryPoints.set(`${id}\n${route}`, { symbol_id: id, kind: 'page', display: route }));
      }
    }
  }

  return {
    symbols: [...symbols.values()].sort((a, b) => ordinal(a.id, b.id)),
    edges: [...edges.values()].filter((edge) => symbols.has(edge.to)).sort((a, b) => ordinal(a.from, b.from) || ordinal(a.to, b.to)),
    entry_points: [...entryPoints.values()].filter((entry) => symbols.has(entry.symbol_id)).sort((a, b) => ordinal(a.symbol_id, b.symbol_id)),
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
  };
}

function createMapper(ts, checker, repoRoot) {
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
      }
    }

    return found;
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
    };
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

  return { repoPath, declarations, symbol, callSites, defaultExportIds };
}

const AXIOS_VERBS = new Set(['get', 'post', 'put', 'patch', 'delete', 'head', 'options']);

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
