const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { runInNewContext } = require('node:vm');
const assert = require('node:assert/strict');
const ts = require('typescript');

function load(file) {
  const source = readFileSync(resolve(__dirname, '..', file), 'utf8');
  const js = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText;
  const exports = {};
  runInNewContext(js, { exports, require() { throw new Error('unexpected import'); } });
  return exports;
}

const { packQuantityLabel } = load('src/app/core/pack-contents.ts');
assert.equal(packQuantityLabel(1, 1), '1 por pack');
assert.equal(packQuantityLabel(1, 2), '1 por pack · 2 total');
assert.equal(packQuantityLabel(2, 3), '2 por pack · 6 total');
console.log('OK: cantidades de componentes de pack.');
