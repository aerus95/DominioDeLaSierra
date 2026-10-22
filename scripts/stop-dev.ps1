#requires -Version 5.1
<#
  Detiene solo los procesos registrados por scripts/start-dev.ps1.
  No ejecuta docker compose down, no borra volúmenes y no toca la base de datos.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $candidate = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $compose = Join-Path $candidate "docker-compose.yml"
    $api = Join-Path $candidate "C#\src\DominioDeLaSierra.Api\DominioDeLaSierra.Api.csproj"
    if (-not (Test-Path -LiteralPath $compose) -or -not (Test-Path -LiteralPath $api)) {
        throw "No se reconoce la raíz del repositorio a partir de $PSScriptRoot."
    }

    return $candidate
}

function Get-PropertyValue {
    param($Object, [string]$Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-ProcessCommandLine {
    param([int]$ProcessId)

    if ($ProcessId -le 0) {
        return $null
    }

    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }

    return [string]$process.CommandLine
}

function Stop-OwnedProcess {
    param([int]$ProcessId, [string]$Token)

    if ($ProcessId -le 0) {
        return $false
    }

    $command = Get-ProcessCommandLine $ProcessId
    if ($null -eq $command) {
        return $false
    }

    if ($command.IndexOf($Token, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Write-Host "No se detiene el PID $ProcessId porque ya no es el proceso esperado."
        return $false
    }

    & taskkill.exe /PID $ProcessId /T /F | Out-Null
    if ($LASTEXITCODE -eq 0 -or $null -eq (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        Write-Host "Detenido PID $ProcessId ($Token)."
        return $true
    }

    Write-Host "No se ha podido detener el PID $ProcessId."
    return $false
}

function Read-ChildPid {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    $text = (Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue)
    if ($null -eq $text) {
        return 0
    }

    $processId = 0
    if ([int]::TryParse($text.Trim(), [ref]$processId)) {
        return $processId
    }

    return 0
}

$repoRoot = Get-RepoRoot
$statePath = Join-Path $repoRoot ".dev\state.json"
if (-not (Test-Path -LiteralPath $statePath)) {
    Write-Host "No hay un arranque registrado por start-dev.ps1. No se detiene ningún proceso."
    exit 0
}

$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$services = Get-PropertyValue $state "services"
$names = @("cloudflared", "relay", "angular", "api")
foreach ($name in $names) {
    $service = Get-PropertyValue $services $name
    if ($null -eq $service) {
        Write-Host "$name no lo ha arrancado este script."
        continue
    }

    $supervisorPid = 0
    $rawSupervisor = Get-PropertyValue $service "supervisorPid"
    if ($null -ne $rawSupervisor) {
        [void][int]::TryParse([string]$rawSupervisor, [ref]$supervisorPid)
    }

    $childPid = Read-ChildPid ([string](Get-PropertyValue $service "childPidFile"))
    $childToken = [string](Get-PropertyValue $service "childCommandToken")
    if ([string]::IsNullOrWhiteSpace($childToken)) {
        $childToken = "dev-supervisor.ps1"
    }

    Stop-OwnedProcess -ProcessId $supervisorPid -Token "dev-supervisor.ps1" | Out-Null
    if ($childPid -gt 0 -and $null -ne (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) {
        Stop-OwnedProcess -ProcessId $childPid -Token $childToken | Out-Null
    }
}

$docker = Get-PropertyValue $state "docker"
$stoppedPostgres = $false
$stoppedMailpit = $false
if ($null -ne $docker -and [bool](Get-PropertyValue $docker "postgresStartedByScript")) {
    Write-Host "Deteniendo el contenedor de PostgreSQL que arrancó start-dev.ps1. El volumen se conserva."
    & docker stop dominio-sierra-postgres | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $stoppedPostgres = $true
    }
}
else {
    Write-Host "PostgreSQL no lo arrancó este script. El contenedor se deja como está."
}

if ($null -ne $docker -and [bool](Get-PropertyValue $docker "mailpitStartedByScript")) {
    Write-Host "Deteniendo Mailpit. No se borra ningún volumen."
    & docker stop dominio-sierra-mailpit | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $stoppedMailpit = $true
    }
}
else {
    Write-Host "Mailpit no lo arrancó este script. El contenedor se deja como está."
}

$replacement = [pscustomobject]@{
    version = 1
    services = [pscustomobject]@{}
    docker = [pscustomobject]@{
        postgresStartedByScript = $false
        mailpitStartedByScript = $false
    }
}
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($statePath, ($replacement | ConvertTo-Json -Depth 5), $utf8)
Write-Host "Los procesos registrados se han detenido."
if (-not $stoppedPostgres) {
    Write-Host "PostgreSQL sigue dependiendo de cómo estuviera antes de start-dev.ps1."
}
if (-not $stoppedMailpit) {
    Write-Host "Mailpit sigue dependiendo de cómo estuviera antes de start-dev.ps1."
}
exit 0
