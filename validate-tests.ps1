$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet test (Join-Path $root 'C#\DominioDeLaSierra.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Push-Location (Join-Path $root 'e2e\playwright')
try {
  npx playwright test
  exit $LASTEXITCODE
}
finally {
  Pop-Location
}
