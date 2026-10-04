$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root
dotnet run --project 'C#\tests\DominioDeLaSierra.TestDatabase\DominioDeLaSierra.TestDatabase.csproj' -- prepare
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$env:ASPNETCORE_ENVIRONMENT = 'Testing'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5142'
dotnet run --project 'C#\src\DominioDeLaSierra.Api\DominioDeLaSierra.Api.csproj' --no-launch-profile
