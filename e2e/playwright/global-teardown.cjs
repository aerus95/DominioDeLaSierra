const { execFileSync } = require('node:child_process');
const { rmSync } = require('node:fs');
const os = require('node:os');
const path = require('node:path');

module.exports = function teardown() {
  const root = path.resolve(__dirname, '../..');
  const project = path.join(root, 'C#', 'tests', 'DominioDeLaSierra.TestDatabase', 'DominioDeLaSierra.TestDatabase.csproj');
  execFileSync('dotnet', ['run', '--project', project, '--', 'cleanup'], {
    cwd: root,
    stdio: 'inherit',
  });
  rmSync(path.join(os.tmpdir(), 'dominio-sierra-tests'), { recursive: true, force: true });
};
