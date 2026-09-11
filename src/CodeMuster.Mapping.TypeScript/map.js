'use strict';

const crypto = require('crypto');
const path = require('path');

const chunks = [];
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => chunks.push(chunk));
process.stdin.on('end', () => {
  process.stdout.write(JSON.stringify(mapRepo(JSON.parse(chunks.join('')))));
});

function mapRepo(request) {
  const repoRoot = path.resolve(request.repo_root);
  const included = new Set(request.paths);
  const mapped = new Set();
  const symbols = new Map();
  const edges = new Map();
  const entryPoints = new Map();
  const unresolvedNames = new Map();
  let resolved = 0;
  let unresolved = 0;

  for (const tsconfig of request.tsconfigs) {
    const configPath = path.join(repoRoot, tsconfig);
    const ts = require(require.resolve('typescript', { paths: [path.dirname(configPath)] }));
    const config = ts.readConfigFile(configPath, ts.sys.readFile).config;
    const parsed = ts.parseJsonConfigFileContent(config, ts.sys, path.dirname(configPath), undefined, configPath);
    const program = ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options });
    const mapper = createMapper(ts, program.getTypeChecker(), repoRoot);

    for (const sourceFile of program.getSourceFiles()) {
      const file = mapper.repoPath(sourceFile);
      if (sourceFile.isDeclarationFile || !included.has(file) || mapped.has(file)) {
        continue;
      }

      mapped.add(file);
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
    diagnostics: [],
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
    const calls = { resolved: 0, unresolved: [], targets: new Set() };
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
      return `${file()}#${className(parent)}.${ts.isConstructorDeclaration(node) ? 'constructor' : node.name.getText()}`;
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
