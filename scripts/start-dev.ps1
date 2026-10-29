#requires -Version 5.1
<#
  Arranque local de Dominio de la Sierra.
  No hace git pull, commit ni push. No instala herramientas. No aplica migraciones.
  No guarda credenciales y no las pone en la línea de comandos.
#>
[CmdletBinding()]
param(
    [ValidateSet("Cloudflare", "Redsys")]
    [string]$Only,

    [switch]$WithRedsys
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:ShellPath = (Get-Process -Id $PID).Path
$script:RepoRoot = $null
$script:DevRoot = $null
$script:LogRoot = $null
$script:StatePath = $null
$script:SupervisorPath = $null
$script:SuggestedWebhookId = "9UC40709PV7468705"
$script:WebhookPath = "/api/v1/checkout/paypal/webhook"
$script:RedsysPath = "/api/v1/checkout/redsys/notification"
$script:RedsysUrlOk = "http://localhost:4200/checkout/exito"
$script:RedsysUrlKo = "http://localhost:4200/checkout/cancelado"
$script:TunnelOrigin = $null
$script:RemovedEnvironmentNames = @(
    "PayPal__ClientId",
    "PayPal__ClientSecret",
    "PayPal__WebhookId",
    "PayPal__Environment",
    "Stripe__SecretKey",
    "STRIPE_SECRET_KEY",
    "Stripe__WebhookSecret",
    "STRIPE_WEBHOOK_SECRET",
    "Stripe__Gateway",
    "Redsys__Enabled",
    "Redsys__Environment",
    "Redsys__MerchantCode",
    "Redsys__Terminal",
    "Redsys__SecretKey",
    "Redsys__SignatureVersion",
    "Redsys__MerchantUrl",
    "Redsys__UrlOk",
    "Redsys__UrlKo",
    "Database__Password",
    "POSTGRES_PASSWORD"
)
$script:SensitiveValues = New-Object System.Collections.Generic.List[string]
$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Report = @{
    Postgres = "ERROR"
    Mailpit = "ERROR"
    ApiUrl = "http://localhost:5141"
    AdminUrl = "http://localhost:5141/admin/login"
    AngularUrl = "http://localhost:4200"
    RelayUrl = "http://127.0.0.1:5088"
    PayPal = "Sandbox"
    WebhookId = "pendiente"
    WebhookUrl = "no disponible"
    ClientId = "pendiente"
    Api = "detenida"
    Angular = "detenida"
    Relay = "detenido"
    Cloudflare = "detenido"
    Redsys = "desactivado"
    MerchantUrl = "no aplica"
}

function Write-Info {
    param([string]$Message)
    Write-Host $Message
}

function Write-WarnLine {
    param([string]$Message)
    Write-Host "AVISO: $Message"
}

function Write-Fail {
    param([string]$Message, [string]$Retry)

    Write-Host "ERROR: $Message"
    $script:Failures.Add($Message) | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($Retry)) {
        Write-Host "       $Retry"
        $script:Failures.Add($Retry) | Out-Null
    }
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

function Get-RepoRoot {
    $candidate = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $compose = Join-Path $candidate "docker-compose.yml"
    $api = Join-Path $candidate "C#\src\DominioDeLaSierra.Api\DominioDeLaSierra.Api.csproj"
    if (-not (Test-Path -LiteralPath $compose) -or -not (Test-Path -LiteralPath $api)) {
        throw "No se reconoce la raíz del repositorio a partir de $PSScriptRoot."
    }

    return $candidate
}

function Test-ConsoleInteractive {
    try {
        return -not [Console]::IsInputRedirected
    }
    catch {
        return $false
    }
}

function Read-DevState {
    if (-not (Test-Path -LiteralPath $script:StatePath)) {
        return [pscustomobject]@{
            version = 1
            services = [pscustomobject]@{}
            docker = [pscustomobject]@{
                postgresStartedByScript = $false
                mailpitStartedByScript = $false
            }
        }
    }

    return Get-Content -LiteralPath $script:StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Save-DevState {
    param($State)

    $directory = Split-Path -Parent $script:StatePath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $json = $State | ConvertTo-Json -Depth 6
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($script:StatePath, $json, $utf8)
}

function Set-ServiceState {
    param($State, [string]$Name, $Value)

    $services = Get-PropertyValue $State "services"
    if ($null -eq $services) {
        $services = [pscustomobject]@{}
        $State | Add-Member -NotePropertyName "services" -NotePropertyValue $services -Force
    }

    $existing = $services.PSObject.Properties[$Name]
    if ($null -ne $existing) {
        $existing.Value = $Value
    }
    else {
        $services | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }

    Save-DevState $State
}

function Get-ServiceState {
    param($State, [string]$Name)
    $services = Get-PropertyValue $State "services"
    return Get-PropertyValue $services $Name
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

function Test-SupervisorAlive {
    param($ServiceState)

    $processId = 0
    $raw = Get-PropertyValue $ServiceState "supervisorPid"
    if ($null -eq $raw -or -not [int]::TryParse([string]$raw, [ref]$processId)) {
        return $false
    }

    $command = Get-ProcessCommandLine $processId
    return $null -ne $command -and $command.IndexOf("dev-supervisor.ps1", [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Get-HttpStatusCode {
    param([string]$Url, [string]$Method = "GET", [int]$TimeoutMs = 5000)

    $request = $null
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Method = $Method
        $request.Timeout = $TimeoutMs
        $request.AllowAutoRedirect = $false
        $response = $request.GetResponse()
        return [int]$response.StatusCode
    }
    catch [System.Net.WebException] {
        $webResponse = $_.Exception.Response
        if ($null -ne $webResponse) {
            $code = [int]$webResponse.StatusCode
            $webResponse.Close()
            return $code
        }

        return 0
    }
    catch {
        return 0
    }
    finally {
        if ($null -ne $response) {
            $response.Close()
        }
    }
}

function Wait-HttpStatusCode {
    param(
        [string]$Url,
        [int[]]$Expected,
        [int]$TimeoutSec,
        [string]$Method = "GET"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $status = 0
    do {
        $status = Get-HttpStatusCode -Url $Url -Method $Method
        if ($Expected -contains $status) {
            return $status
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    return $status
}

function Test-TcpPort {
    param([int]$Port)

    $targets = @(
        @{ Family = [System.Net.Sockets.AddressFamily]::InterNetwork; Address = [System.Net.IPAddress]::Loopback },
        @{ Family = [System.Net.Sockets.AddressFamily]::InterNetworkV6; Address = [System.Net.IPAddress]::IPv6Loopback }
    )
    foreach ($target in $targets) {
        $client = $null
        try {
            $client = New-Object System.Net.Sockets.TcpClient($target.Family)
            $wait = $client.BeginConnect($target.Address, $Port, $null, $null)
            if ($wait.AsyncWaitHandle.WaitOne(800) -and $client.Connected) {
                $client.EndConnect($wait)
                return $true
            }
        }
        catch {
        }
        finally {
            if ($null -ne $client) {
                $client.Close()
            }
        }
    }

    return $false
}

function Invoke-Native {
    param(
        [string]$File,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $File
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $quoted = New-Object System.Collections.Generic.List[string]
    foreach ($argument in $Arguments) {
        if ($argument -match '[\s"]') {
            $quoted.Add('"' + ($argument -replace '"', '\"') + '"') | Out-Null
        }
        else {
            $quoted.Add($argument) | Out-Null
        }
    }

    $startInfo.Arguments = ($quoted -join " ")
    foreach ($name in $script:RemovedEnvironmentNames) {
        [void]$startInfo.EnvironmentVariables.Remove($name)
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return @{
        ExitCode = $process.ExitCode
        Stdout = $stdoutTask.Result
        Stderr = $stderrTask.Result
    }
}

function Write-RedactedOutput {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return
    }

    foreach ($line in ($Text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }

        $hidden = $trimmed -match 'PASSWORD|ClientSecret|ClientId|WebhookId|Secret'
        if (-not $hidden) {
            foreach ($value in $script:SensitiveValues) {
                if (-not [string]::IsNullOrEmpty($value) -and $trimmed.Contains($value)) {
                    $hidden = $true
                }
            }
        }

        if ($hidden) {
            Write-Host "  [salida omitida]"
        }
        else {
            Write-Host "  $trimmed"
        }
    }
}

function Test-PayPalValue {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return $false
    }

    if ($Value.Length -lt 8 -or $Value.Length -gt 127) {
        return $false
    }

    if ($Value -match '\s') {
        return $false
    }

    if ($Value -match '^(?i:live)$') {
        return $false
    }

    return $true
}

function ConvertTo-PlainString {
    param([Security.SecureString]$Secure)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Get-ValueHint {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value) -or $Value.Length -lt 4) {
        return "configurado"
    }

    return "configurado (..." + $Value.Substring($Value.Length - 4) + ")"
}

function Resolve-RequiredCommand {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Resolve-CloudflaredPath {
    $command = Resolve-RequiredCommand "cloudflared"
    if ($null -ne $command) {
        return $command
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "cloudflared\cloudflared.exe"),
        (Join-Path $env:ProgramFiles "cloudflared\cloudflared.exe")
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Test-DotNet10Sdk {
    $result = Invoke-Native -File $script:DotNetPath -Arguments @("--list-sdks") -WorkingDirectory $script:RepoRoot
    return $result.ExitCode -eq 0 -and $result.Stdout -match '(?m)^10\.'
}

function Test-EnvFileHasPassword {
    $path = Join-Path $script:RepoRoot ".env"
    if (-not (Test-Path -LiteralPath $path)) {
        return $false
    }

    foreach ($raw in [System.IO.File]::ReadAllLines($path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith("#")) {
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim().Trim('"')
        if ($name -eq "POSTGRES_PASSWORD" -and -not [string]::IsNullOrWhiteSpace($value)) {
            return $true
        }
    }

    return $false
}

function Read-SharedText {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    $reader = New-Object System.IO.StreamReader($stream, $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Get-SafeLogTail {
    param([string]$Path, [int]$Count = 25)

    $text = Read-SharedText $Path
    if ([string]::IsNullOrWhiteSpace($text)) {
        return @()
    }

    $all = @($text -split "`r?`n")
    $start = [Math]::Max(0, $all.Count - $Count)
    $lines = @($all[$start..($all.Count - 1)])
    $safe = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        $hidden = $line -match 'PASSWORD|ClientSecret|ClientId|WebhookId|SecretKey'
        if (-not $hidden) {
            foreach ($value in $script:SensitiveValues) {
                if (-not [string]::IsNullOrEmpty($value) -and $line.Contains($value)) {
                    $hidden = $true
                }
            }
        }

        if ($hidden) {
            $safe.Add("  [línea omitida]") | Out-Null
        }
        else {
            $safe.Add("  $line") | Out-Null
        }
    }

    return $safe
}

function Clear-SensitiveValues {
    $script:SensitiveValues.Clear()
}

function Read-PayPalCredentials {
    $environment = [Environment]::GetEnvironmentVariable("PayPal__Environment", "Process")
    if (-not [string]::IsNullOrWhiteSpace($environment) -and $environment.Trim() -ne "Sandbox") {
        Write-Fail "PayPal__Environment solo puede ser Sandbox." "Quita ese valor del proceso y vuelve a ejecutar .\scripts\start-dev.ps1."
        return $null
    }

    $clientId = [Environment]::GetEnvironmentVariable("PayPal__ClientId", "Process")
    $clientSecret = [Environment]::GetEnvironmentVariable("PayPal__ClientSecret", "Process")
    $webhookId = [Environment]::GetEnvironmentVariable("PayPal__WebhookId", "Process")
    $interactive = Test-ConsoleInteractive

    if (-not (Test-PayPalValue $clientId)) {
        if (-not $interactive) {
            Write-Fail "Falta el Client ID de PayPal Sandbox y esta terminal no es interactiva." "Ejecuta .\scripts\start-dev.ps1 en la terminal de Cursor. No pegues el secreto en el chat ni en un comando."
            return $null
        }

        $clientId = (Read-Host "Client ID de PayPal Sandbox").Trim()
    }

    if (-not (Test-PayPalValue $clientSecret)) {
        if (-not $interactive) {
            Write-Fail "Falta el Client Secret de PayPal Sandbox y esta terminal no es interactiva." "Ejecuta .\scripts\start-dev.ps1 en la terminal de Cursor. El secreto se pide oculto y no queda en el historial."
            return $null
        }

        $secure = Read-Host -AsSecureString -Prompt "Client Secret de PayPal Sandbox"
        try {
            $clientSecret = ConvertTo-PlainString $secure
        }
        finally {
            $secure.Dispose()
        }
    }

    if (-not (Test-PayPalValue $webhookId)) {
        if ($interactive) {
            $entered = Read-Host "Webhook ID de Sandbox [$($script:SuggestedWebhookId)]"
            if ([string]::IsNullOrWhiteSpace($entered)) {
                $webhookId = $script:SuggestedWebhookId
            }
            else {
                $webhookId = $entered.Trim()
            }
        }
        else {
            $webhookId = $script:SuggestedWebhookId
            Write-Info "Webhook ID: se usa el identificador sugerido porque la terminal no es interactiva."
        }
    }

    if (-not (Test-PayPalValue $clientId) -or -not (Test-PayPalValue $clientSecret) -or -not (Test-PayPalValue $webhookId)) {
        Write-Fail "Las credenciales de PayPal Sandbox no cumplen el formato que acepta la API." "Vuelve a ejecutar .\scripts\start-dev.ps1 e introduce los valores de la aplicación Sandbox."
        return $null
    }

    $script:SensitiveValues.Add($clientId) | Out-Null
    $script:SensitiveValues.Add($clientSecret) | Out-Null
    $script:SensitiveValues.Add($webhookId) | Out-Null
    $script:Report.ClientId = Get-ValueHint $clientId
    $script:Report.WebhookId = "configurado"
    return @{
        ClientId = $clientId
        ClientSecret = $clientSecret
        WebhookId = $webhookId
    }
}

function Test-RedsysParentEnvironment {
    $environment = [Environment]::GetEnvironmentVariable("Redsys__Environment", "Process")
    if ([string]::IsNullOrWhiteSpace($environment) -or $environment.Trim() -eq "Test") {
        return $true
    }

    Write-Fail "Redsys__Environment solo puede ser Test." "Quita ese valor del proceso. El arranque no usa LIVE."
    return $false
}

function Test-RedsysMerchantCode {
    param([string]$Value)

    return $Value -match '^[0-9]{9}$'
}

function Test-RedsysTerminal {
    param([string]$Value)

    return $Value -match '^[0-9]{1,3}$'
}

function Test-RedsysSecret {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value) -or $Value.Length -gt 256) {
        return $false
    }

    if ($Value -match '\s') {
        return $false
    }

    if ($Value -match '^(?i:live|test)$') {
        return $false
    }

    return $true
}

function New-RedsysMerchantUrl {
    param([string]$Origin)

    if ([string]::IsNullOrWhiteSpace($Origin)) {
        return $null
    }

    $trimmed = $Origin.Trim().TrimEnd("/")
    if ($trimmed -notmatch '^https://[a-z0-9-]+\.trycloudflare\.com$') {
        return $null
    }

    return $trimmed + $script:RedsysPath
}

function Read-RedsysCredentials {
    param([string]$MerchantUrl)

    if (-not (Test-RedsysParentEnvironment)) {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace((New-RedsysMerchantUrl $MerchantUrl.Replace($script:RedsysPath, "")))) {
        Write-Fail "No hay una URL de túnel actual para la notificación de Redsys." "No se habilita Redsys y no se reinicia la API."
        return $null
    }

    if (-not (Test-ConsoleInteractive)) {
        Write-Fail "Faltan las credenciales TEST de Redsys y esta terminal no es interactiva." "Ejecuta .\scripts\start-dev.ps1 -WithRedsys en la terminal de Cursor. No pegues la clave en el chat ni en un comando."
        return $null
    }

    $merchantCode = (Read-Host "Código de comercio TEST de Redsys (9 dígitos)").Trim()
    $terminal = (Read-Host "Terminal TEST de Redsys").Trim()
    $secure = Read-Host -AsSecureString -Prompt "Clave de firma TEST de Redsys"
    $secret = $null
    try {
        $secret = ConvertTo-PlainString $secure
    }
    finally {
        $secure.Dispose()
    }

    if (-not (Test-RedsysMerchantCode $merchantCode) -or -not (Test-RedsysTerminal $terminal) -or -not (Test-RedsysSecret $secret)) {
        Write-Fail "El comercio, el terminal o la clave TEST no cumplen el formato de Redsys." "Vuelve a ejecutar el arranque e introdúcelos en la terminal. No se ha guardado nada."
        return $null
    }

    $script:SensitiveValues.Add($secret) | Out-Null
    $script:Report.Redsys = "TEST"
    $script:Report.MerchantUrl = $MerchantUrl
    return @{
        MerchantCode = $merchantCode
        Terminal = $terminal
        SecretKey = $secret
        MerchantUrl = $MerchantUrl
    }
}

function New-ApiEnvironment {
    param($Credentials, $Redsys)

    $environment = @{
        ASPNETCORE_ENVIRONMENT = "Development"
        ASPNETCORE_URLS = "http://localhost:5141"
        "Database__Host" = "127.0.0.1"
        "Database__Name" = "dominio_sierra"
        "PayPal__Enabled" = "true"
        "PayPal__Environment" = "Sandbox"
        "PayPal__ClientId" = [string]$Credentials.ClientId
        "PayPal__ClientSecret" = [string]$Credentials.ClientSecret
        "PayPal__WebhookId" = [string]$Credentials.WebhookId
        "Notifications__DispatchEnabled" = "false"
        "Reservations__SweepEnabled" = "false"
        "Reservations__ProviderReconciliationEnabled" = "false"
        "Reservations__HoldAlertEnabled" = "false"
        "InvoiceIssuance__Enabled" = "false"
        "Redsys__Enabled" = "false"
        DOTNET_NOLOGO = "1"
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    }

    if ($null -eq $Redsys) {
        return $environment
    }

    $merchantUrl = [string]$Redsys.MerchantUrl
    $operational = $environment.ASPNETCORE_ENVIRONMENT -eq "Development" `
        -and (Test-RedsysMerchantCode ([string]$Redsys.MerchantCode)) `
        -and (Test-RedsysTerminal ([string]$Redsys.Terminal)) `
        -and (Test-RedsysSecret ([string]$Redsys.SecretKey)) `
        -and $merchantUrl -eq (New-RedsysMerchantUrl ($merchantUrl.Replace($script:RedsysPath, "")))
    if (-not $operational) {
        Write-Fail "La configuración TEST de Redsys está incompleta." "La API no se arranca con Redsys habilitado."
        return $null
    }

    $environment["Redsys__Enabled"] = "true"
    $environment["Redsys__Environment"] = "Test"
    $environment["Redsys__MerchantCode"] = [string]$Redsys.MerchantCode
    $environment["Redsys__Terminal"] = [string]$Redsys.Terminal
    $environment["Redsys__SecretKey"] = [string]$Redsys.SecretKey
    $environment["Redsys__SignatureVersion"] = "HMAC_SHA512_V2"
    $environment["Redsys__MerchantUrl"] = $merchantUrl
    $environment["Redsys__UrlOk"] = $script:RedsysUrlOk
    $environment["Redsys__UrlKo"] = $script:RedsysUrlKo
    return $environment
}

function Set-RedsysLaunchState {
    param($State, [bool]$Enabled, [string]$Origin)

    $value = [pscustomobject]@{
        enabled = $Enabled
        merchantOrigin = $(if ($Enabled) { $Origin } else { "" })
    }
    $existing = $State.PSObject.Properties["redsys"]
    if ($null -ne $existing) {
        $existing.Value = $value
    }
    else {
        $State | Add-Member -NotePropertyName "redsys" -NotePropertyValue $value -Force
    }

    Save-DevState $State
}

function Watch-RedsysMerchantUrl {
    param($State, [string]$Origin)

    $redsys = Get-PropertyValue $State "redsys"
    $enabled = Get-PropertyValue $redsys "enabled"
    $stored = [string](Get-PropertyValue $redsys "merchantOrigin")
    if ($enabled -ne $true -or [string]::IsNullOrWhiteSpace($stored) -or $stored -eq $Origin) {
        return
    }

    $script:Report.Redsys = "MerchantUrl obsoleta"
    $script:Report.MerchantUrl = $stored.TrimEnd("/") + $script:RedsysPath
    Write-WarnLine "La MerchantUrl de Redsys sigue en $stored y el túnel actual es $Origin."
    Write-Info "La API no se reinicia en este paso. PostgreSQL, Angular, el relay y PayPal siguen."
    Write-Info "Recuperación, solo la API: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -Only Redsys"
}

function Clear-CredentialBag {
    param($Bag)

    if ($null -eq $Bag) {
        return
    }

    foreach ($name in @($Bag.Keys)) {
        $Bag[$name] = $null
    }
}

function Start-SupervisedProcess {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [hashtable]$Environment,
        [string]$ChildCommandToken
    )

    $stdoutLog = Join-Path $script:LogRoot "$Name.out.log"
    $stderrLog = Join-Path $script:LogRoot "$Name.err.log"
    $childPidFile = Join-Path $script:DevRoot "pids\$Name.pid"
    New-Item -ItemType Directory -Force -Path $script:LogRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $childPidFile) | Out-Null
    foreach ($logFile in @($stdoutLog, $stderrLog)) {
        $cleared = New-Object System.IO.FileStream(
            $logFile,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::ReadWrite)
        $cleared.Dispose()
    }
    if (Test-Path -LiteralPath $childPidFile) {
        Remove-Item -LiteralPath $childPidFile -Force
    }

    $order = @{
        filePath = $FilePath
        arguments = @($Arguments)
        workingDirectory = $WorkingDirectory
        stdoutLog = $stdoutLog
        stderrLog = $stderrLog
        childPidFile = $childPidFile
        remove = @($script:RemovedEnvironmentNames)
        set = $Environment
    }
    $json = $order | ConvertTo-Json -Depth 6 -Compress
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    $json = $null

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $script:ShellPath
    $startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$($script:SupervisorPath)`""
    $startInfo.WorkingDirectory = $script:RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($secretName in $script:RemovedEnvironmentNames) {
        [void]$startInfo.EnvironmentVariables.Remove($secretName)
    }

    $supervisor = New-Object System.Diagnostics.Process
    $supervisor.StartInfo = $startInfo
    [void]$supervisor.Start()
    $supervisor.StandardInput.WriteLine($encoded)
    $supervisor.StandardInput.Close()
    $encoded = $null

    $deadline = (Get-Date).AddSeconds(20)
    do {
        if ((Test-Path -LiteralPath $childPidFile) -or $supervisor.HasExited) {
            break
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    if ($supervisor.HasExited -and -not (Test-Path -LiteralPath $childPidFile)) {
        Write-Info "El supervisor de $Name terminó antes de crear el proceso."
        Get-SafeLogTail $stderrLog | ForEach-Object { Write-Host $_ }
        return $null
    }

    if (-not $supervisor.HasExited) {
        $supervisor.StandardOutput.Close()
        $supervisor.StandardError.Close()
    }

    return @{
        supervisorPid = $supervisor.Id
        stdoutLog = $stdoutLog
        stderrLog = $stderrLog
        childPidFile = $childPidFile
        childCommandToken = $ChildCommandToken
    }
}

function Test-ContainerRunning {
    param([string]$Name)

    $result = Invoke-Native -File "docker" -Arguments @("inspect", "-f", "{{.State.Status}}", $Name) -WorkingDirectory $script:RepoRoot
    return $result.ExitCode -eq 0 -and $result.Stdout.Trim() -eq "running"
}

function Wait-Postgres {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $ready = Invoke-Native -File "docker" -Arguments @(
            "exec", "dominio-sierra-postgres", "pg_isready", "-U", "dominio_sierra_app", "-d", "dominio_sierra"
        ) -WorkingDirectory $script:RepoRoot
        if ($ready.ExitCode -eq 0) {
            return $true
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Start-DevDocker {
    param($State)

    Write-Info ""
    Write-Info "[1/5] Docker"
    if (-not (Test-EnvFileHasPassword)) {
        Write-Fail "Falta POSTGRES_PASSWORD en el archivo .env de la raíz." "Créalo fuera de Git. El script no inventa ni muestra esa contraseña."
        return $false
    }

    $docker = Get-PropertyValue $State "docker"
    if ($null -eq $docker) {
        $docker = [pscustomobject]@{
            postgresStartedByScript = $false
            mailpitStartedByScript = $false
        }
        $State | Add-Member -NotePropertyName "docker" -NotePropertyValue $docker -Force
    }

    $postgresRunning = Test-ContainerRunning "dominio-sierra-postgres"
    if ($postgresRunning) {
        Write-Info "PostgreSQL ya estaba en marcha."
    }
    else {
        Write-Info "Arrancando PostgreSQL sin tocar el volumen."
        $up = Invoke-Native -File "docker" -Arguments @("compose", "up", "-d", "postgres") -WorkingDirectory $script:RepoRoot
        if ($up.ExitCode -ne 0) {
            Write-Fail "Docker no ha podido arrancar PostgreSQL." "Revisa Docker Desktop y vuelve a ejecutar .\scripts\start-dev.ps1."
            Write-RedactedOutput $up.Stderr
            return $false
        }

        $docker.postgresStartedByScript = $true
        Save-DevState $State
    }

    if (Wait-Postgres) {
        $script:Report.Postgres = "OK"
        Write-Info "PostgreSQL responde en 127.0.0.1:5432."
    }
    else {
        Write-Fail "PostgreSQL no ha respondido a tiempo." "Puedes reintentar con .\scripts\start-dev.ps1. No se han aplicado migraciones."
        return $false
    }

    $mailpitRunning = Test-ContainerRunning "dominio-sierra-mailpit"
    if ($mailpitRunning) {
        Write-Info "Mailpit ya estaba en marcha."
    }
    else {
        Write-Info "Arrancando Mailpit."
        $up = Invoke-Native -File "docker" -Arguments @("compose", "-f", "docker-compose.mailpit.yml", "up", "-d") -WorkingDirectory $script:RepoRoot
        if ($up.ExitCode -ne 0) {
            Write-Fail "Docker no ha podido arrancar Mailpit." "La API puede arrancar igual. Revisa el compose de Mailpit y vuelve a ejecutar el script."
            Write-RedactedOutput $up.Stderr
            $script:Report.Mailpit = "ERROR"
            return $true
        }

        $docker.mailpitStartedByScript = $true
        Save-DevState $State
    }

    $mailpit = Wait-HttpStatusCode -Url "http://127.0.0.1:8025/" -Expected @(200) -TimeoutSec 30
    if ($mailpit -eq 200) {
        $script:Report.Mailpit = "OK"
        Write-Info "Mailpit responde en http://127.0.0.1:8025."
    }
    else {
        $script:Report.Mailpit = "ERROR"
        Write-Fail "Mailpit no responde en http://127.0.0.1:8025." "El resto puede continuar. El envío de avisos queda desactivado."
    }

    return $true
}

function Stop-RecordedOwnedService {
    param($State, [string]$Name, [string]$ChildToken, [int]$Port)

    $service = Get-ServiceState $State $Name
    if ($null -eq $service) {
        return -not (Test-TcpPort -Port $Port)
    }

    $supervisorPid = 0
    $rawSupervisor = Get-PropertyValue $service "supervisorPid"
    if ($null -ne $rawSupervisor) {
        [void][int]::TryParse([string]$rawSupervisor, [ref]$supervisorPid)
    }

    $childPid = 0
    $childPidFile = [string](Get-PropertyValue $service "childPidFile")
    if (-not [string]::IsNullOrWhiteSpace($childPidFile)) {
        $text = Read-SharedText $childPidFile
        [void][int]::TryParse($text.Trim(), [ref]$childPid)
    }

    if ($supervisorPid -gt 0) {
        $command = Get-ProcessCommandLine $supervisorPid
        if ($null -ne $command -and $command.IndexOf("dev-supervisor.ps1", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            & taskkill.exe /PID $supervisorPid /T /F | Out-Null
        }
    }

    if ($childPid -gt 0) {
        $command = Get-ProcessCommandLine $childPid
        if ($null -ne $command -and $command.IndexOf($ChildToken, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            & taskkill.exe /PID $childPid /T /F | Out-Null
        }
    }

    $services = Get-PropertyValue $State "services"
    if ($null -ne $services -and $null -ne $services.PSObject.Properties[$Name]) {
        [void]$services.PSObject.Properties.Remove($Name)
    }

    Save-DevState $State
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-TcpPort -Port $Port)) {
            return $true
        }

        Start-Sleep -Milliseconds 400
    }

    return -not (Test-TcpPort -Port $Port)
}

function Start-DevApi {
    param(
        $State,
        $Credentials,
        $Redsys,
        [switch]$RestartOwned,
        [string]$Step = "[2/5] API"
    )

    Write-Info ""
    Write-Info $Step
    $owned = Test-SupervisorAlive (Get-ServiceState $State "api")
    $listening = Test-TcpPort -Port 5141
    if ($listening -and $RestartOwned) {
        if (-not $owned) {
            Write-Fail "La API del puerto 5141 no la controla este script." "No se reinicia y Redsys no se habilita."
            return $false
        }

        if ($null -eq $Credentials -or $null -eq $Redsys) {
            Write-Fail "No se reinicia la API sin PayPal Sandbox y Redsys TEST completos." "El proceso actual sigue en marcha."
            return $false
        }

        Write-Info "Se reinicia solo la API para aplicar el túnel actual. PostgreSQL, Angular, relay y Cloudflare siguen."
        if (-not (Stop-RecordedOwnedService -State $State -Name "api" -ChildToken "launch-profile" -Port 5141)) {
            Write-Fail "La API anterior no ha liberado el puerto 5141." "No se arranca una segunda API."
            return $false
        }

        $owned = $false
        $listening = $false
    }

    if ($listening) {
        $status = Get-HttpStatusCode -Url "http://localhost:5141/swagger/index.html"
        if ($owned) {
            $script:Report.Api = "en marcha"
            $script:Report.WebhookId = "configurado"
            $recorded = Get-PropertyValue (Get-PropertyValue $State "redsys") "enabled"
            $recordedOrigin = [string](Get-PropertyValue (Get-PropertyValue $State "redsys") "merchantOrigin")
            if ($recorded -eq $true -and -not [string]::IsNullOrWhiteSpace($recordedOrigin)) {
                $script:Report.Redsys = "TEST"
                $script:Report.MerchantUrl = $recordedOrigin.TrimEnd("/") + $script:RedsysPath
            }

            Write-Info "La API ya estaba en marcha (http://localhost:5141)."
        }
        else {
            $script:Report.Api = "ajena"
            Write-WarnLine "El puerto 5141 ya está ocupado por un proceso que este script no ha arrancado. No se inicia otra API ni se cambian sus variables."
            Write-Info "Si esa API no tiene PayPal Sandbox, detén ese proceso y vuelve a ejecutar .\scripts\start-dev.ps1."
        }

        if ($status -eq 200) {
            Write-Info "La API responde."
        }
        else {
            Write-WarnLine "El puerto 5141 acepta conexiones, pero /swagger/index.html ha respondido $status."
        }

        return $true
    }

    if ($null -eq $Credentials) {
        $script:Report.Api = "detenida"
        return $false
    }

    $redsysMode = $null -ne $Redsys
    if ($redsysMode) {
        Write-Info "Arrancando la API en Development con PayPal Sandbox y Redsys TEST. Los avisos, barridos, conciliaciones y facturas quedan apagados."
    }
    else {
        Write-Info "Arrancando la API en Development. PayPal queda en Sandbox y los avisos, barridos y conciliaciones quedan apagados."
    }

    $environment = New-ApiEnvironment -Credentials $Credentials -Redsys $Redsys
    if ($null -eq $environment) {
        return $false
    }
    $apiDirectory = Join-Path $script:RepoRoot "C#\src\DominioDeLaSierra.Api"
    $started = Start-SupervisedProcess `
        -Name "api" `
        -FilePath $script:DotNetPath `
        -Arguments @("run", "--launch-profile", "http") `
        -WorkingDirectory $apiDirectory `
        -Environment $environment `
        -ChildCommandToken "launch-profile"
    $environment.Clear()
    if ($null -eq $started) {
        Write-Fail "La API no ha arrancado." "Revisa .\.dev\logs\api.err.log y vuelve a ejecutar .\scripts\start-dev.ps1. Los servicios que ya respondan no se duplican."
        return $false
    }

    Set-ServiceState $State "api" $started
    Write-Info "Esperando a la API en http://localhost:5141 ..."
    $status = Wait-HttpStatusCode -Url "http://localhost:5141/swagger/index.html" -Expected @(200) -TimeoutSec 180
    if ($status -ne 200) {
        $script:Report.Api = "error"
        Write-Fail "La API no ha respondido en el puerto 5141 (HTTP $status)." "Revisa .\.dev\logs\api.out.log y .\.dev\logs\api.err.log. Luego ejecuta .\scripts\start-dev.ps1."
        Get-SafeLogTail (Join-Path $script:LogRoot "api.err.log") | ForEach-Object { Write-Host $_ }
        return $false
    }

    $script:Report.Api = "en marcha"
    if ($redsysMode) {
        $script:Report.Redsys = "TEST"
        $script:Report.MerchantUrl = [string]$Redsys.MerchantUrl
        Write-Info "API lista en http://localhost:5141. Redsys TEST usa el túnel actual."
    }
    else {
        $script:Report.Redsys = "desactivado"
        $script:Report.MerchantUrl = "no aplica"
        Set-RedsysLaunchState -State $State -Enabled $false -Origin ""
        Write-Info "API lista en http://localhost:5141."
    }

    return $true
}

function Start-DevAngular {
    param($State)

    Write-Info ""
    Write-Info "[3/5] Angular"
    $nodeVersion = ""
    try {
        $nodeVersion = (& $script:NodePath "-v").Trim()
    }
    catch {
        $nodeVersion = "desconocida"
    }

    if ($nodeVersion -notmatch '^v22\.') {
        Write-WarnLine "Node es $nodeVersion y Angular declara engines.node 22.x. El script no cambia la versión instalada."
    }

    $angularDirectory = Join-Path $script:RepoRoot "Angular\web"
    $ng = Join-Path $angularDirectory "node_modules\@angular\cli\bin\ng.js"
    if (-not (Test-Path -LiteralPath $ng)) {
        if (-not (Test-ConsoleInteractive)) {
            Write-Fail "Faltan las dependencias de Angular." "Ejecuta el script en una terminal interactiva para autorizar npm ci, o entra en Angular\web y ejecútalo tú."
            return $false
        }

        $answer = Read-Host "No está node_modules. ¿Ejecutar npm ci en Angular\web? [s/N]"
        if ($answer -notmatch '^(?i:s|si|sí)$') {
            Write-Fail "Angular no se arranca sin dependencias." "Cuando autorices npm ci, vuelve a ejecutar .\scripts\start-dev.ps1."
            return $false
        }

        $npm = Join-Path (Split-Path -Parent $script:NodePath) "npm.cmd"
        Write-Info "Ejecutando npm ci. Puede tardar unos minutos."
        $install = Invoke-Native -File $npm -Arguments @("ci") -WorkingDirectory $angularDirectory
        if ($install.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ng)) {
            Write-Fail "npm ci no ha dejado Angular listo para arrancar." "Revisa el mensaje anterior y vuelve a ejecutar .\scripts\start-dev.ps1."
            Write-RedactedOutput $install.Stdout
            Write-RedactedOutput $install.Stderr
            return $false
        }
    }

    $owned = Test-SupervisorAlive (Get-ServiceState $State "angular")
    $listening = Test-TcpPort -Port 4200

    if ($listening) {
        $script:Report.Angular = $(if ($owned) { "en marcha" } else { "ajena" })
        if ($owned) {
            Write-Info "Angular ya estaba en marcha (http://localhost:4200)."
        }
        else {
            Write-WarnLine "El puerto 4200 ya está ocupado. No se inicia otra tienda."
        }

        return $true
    }

    Write-Info "Arrancando la tienda en http://localhost:4200."
    $started = Start-SupervisedProcess `
        -Name "angular" `
        -FilePath $script:NodePath `
        -Arguments @($ng, "serve", "--host", "localhost", "--port", "4200") `
        -WorkingDirectory $angularDirectory `
        -Environment @{ NG_CLI_ANALYTICS = "false" } `
        -ChildCommandToken "ng.js"
    if ($null -eq $started) {
        Write-Fail "Angular no ha arrancado." "Revisa .\.dev\logs\angular.err.log y vuelve a ejecutar .\scripts\start-dev.ps1."
        return $false
    }

    Set-ServiceState $State "angular" $started
    $status = Wait-HttpStatusCode -Url "http://localhost:4200/" -Expected @(200) -TimeoutSec 180
    if ($status -ne 200) {
        $script:Report.Angular = "error"
        Write-Fail "La tienda no ha respondido en el puerto 4200 (HTTP $status)." "Revisa .\.dev\logs\angular.out.log y vuelve a ejecutar .\scripts\start-dev.ps1."
        Get-SafeLogTail (Join-Path $script:LogRoot "angular.err.log") | ForEach-Object { Write-Host $_ }
        return $false
    }

    $script:Report.Angular = "en marcha"
    Write-Info "Tienda lista en http://localhost:4200."
    return $true
}

function Test-RelayFilter {
    $paypal = Get-HttpStatusCode -Url "http://127.0.0.1:5088$($script:WebhookPath)"
    $redsys = Get-HttpStatusCode -Url "http://127.0.0.1:5088$($script:RedsysPath)"
    $admin = Get-HttpStatusCode -Url "http://127.0.0.1:5088/admin/login"
    $payments = Get-HttpStatusCode -Url "http://127.0.0.1:5088/api/v1/checkout/payments"
    return ($paypal -eq 405 -and $redsys -eq 405 -and $admin -eq 404 -and $payments -eq 404)
}

function Start-DevRelay {
    param($State, [int]$Attempt = 0)

    Write-Info ""
    Write-Info "[4/5] Relay"
    $relayProject = Join-Path $script:RepoRoot "C#\tools\PayPalWebhookRelay\PayPalWebhookRelay.csproj"
    $owned = Test-SupervisorAlive (Get-ServiceState $State "relay")
    $listening = Test-TcpPort -Port 5088
    if ($listening) {
        $script:Report.Relay = $(if ($owned) { "en marcha" } else { "ajeno" })
        if (Test-RelayFilter) {
            if ($owned) {
                Write-Info "El relay ya estaba en marcha (http://127.0.0.1:5088)."
            }
            else {
                Write-WarnLine "El puerto 5088 ya está ocupado. No se inicia otro relay."
            }

            Write-Info "El filtro local admite solo POST del webhook PayPal y POST de la notificación Redsys. /admin y el checkout responden 404."
            return $true
        }

        if ($Attempt -eq 0 -and $owned) {
            Write-Info "El relay en marcha no tiene el filtro actual. Se reinicia solo ese proceso."
            if (-not (Stop-RecordedOwnedService -State $State -Name "relay" -ChildToken "PayPalWebhookRelay" -Port 5088)) {
                Write-Fail "El relay anterior no ha liberado el puerto 5088." "No se arranca un segundo relay."
                return $false
            }

            return Start-DevRelay -State $State -Attempt 1
        }

        $status = Get-HttpStatusCode -Url "http://127.0.0.1:5088$($script:WebhookPath)"
        Write-Fail "El puerto 5088 no aplica el filtro actual (webhook HTTP $status)." "Detén el proceso que ocupa ese puerto y vuelve a ejecutar .\scripts\start-dev.ps1."
        return $false
    }

    Write-Info "Arrancando el relay. Solo reenvía POST $($script:WebhookPath) y POST $($script:RedsysPath)."
    $started = Start-SupervisedProcess `
        -Name "relay" `
        -FilePath $script:DotNetPath `
        -Arguments @("run", "--project", $relayProject) `
        -WorkingDirectory $script:RepoRoot `
        -Environment @{ DOTNET_NOLOGO = "1"; DOTNET_CLI_TELEMETRY_OPTOUT = "1" } `
        -ChildCommandToken "PayPalWebhookRelay"
    if ($null -eq $started) {
        Write-Fail "El relay no ha arrancado." "Revisa .\.dev\logs\relay.err.log y vuelve a ejecutar .\scripts\start-dev.ps1."
        return $false
    }

    Set-ServiceState $State "relay" $started
    $status = Wait-HttpStatusCode -Url "http://127.0.0.1:5088$($script:WebhookPath)" -Expected @(405) -TimeoutSec 120
    if ($status -ne 405 -or -not (Test-RelayFilter)) {
        $script:Report.Relay = "error"
        Write-Fail "El relay no ha aplicado el filtro de PayPal y Redsys (HTTP $status)." "Revisa .\.dev\logs\relay.err.log y vuelve a ejecutar .\scripts\start-dev.ps1."
        Get-SafeLogTail (Join-Path $script:LogRoot "relay.err.log") | ForEach-Object { Write-Host $_ }
        return $false
    }

    $script:Report.Relay = "en marcha"
    Write-Info "Relay listo. Solo admite los dos POST. /admin y el resto de la API responden 404."
    return $true
}

function Get-CurrentTunnelOrigin {
    param([string]$StdoutLog, [string]$StderrLog)

    $chunks = New-Object System.Collections.Generic.List[string]
    foreach ($path in @($StdoutLog, $StderrLog)) {
        if (Test-Path -LiteralPath $path) {
            $chunks.Add((Read-SharedText $path)) | Out-Null
        }
    }

    $text = $chunks -join "`n"
    $requestAt = $text.LastIndexOf("Requesting new quick Tunnel")
    if ($requestAt -lt 0) {
        return $null
    }

    $session = $text.Substring($requestAt)
    $urlMatch = [regex]::Match($session, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if (-not $urlMatch.Success) {
        return $null
    }

    $registeredAt = $session.IndexOf("Registered tunnel connection")
    if ($registeredAt -lt $urlMatch.Index) {
        return $null
    }

    return $urlMatch.Value
}

function Wait-CurrentTunnelOrigin {
    param(
        [string]$StdoutLog,
        [string]$StderrLog,
        [int]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $origin = Get-CurrentTunnelOrigin -StdoutLog $StdoutLog -StderrLog $StderrLog
        if (-not [string]::IsNullOrWhiteSpace($origin)) {
            return $origin
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Test-PublicIPv4 {
    param([string]$Address)

    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($Address, [ref]$parsed)) {
        return $false
    }

    if ($parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        return $false
    }

    $bytes = $parsed.GetAddressBytes()
    if ($bytes[0] -eq 0 -or $bytes[0] -eq 10 -or $bytes[0] -eq 127 -or $bytes[0] -ge 224) {
        return $false
    }

    if ($bytes[0] -eq 169 -and $bytes[1] -eq 254) {
        return $false
    }

    if ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) {
        return $false
    }

    if ($bytes[0] -eq 192 -and $bytes[1] -eq 168) {
        return $false
    }

    return $true
}

function Get-DnsAddresses {
    param(
        [string]$HostName,
        [string]$Server
    )

    $found = New-Object System.Collections.Generic.List[string]
    try {
        $command = @{
            Name = $HostName
            ErrorAction = "Stop"
        }
        if (-not [string]::IsNullOrWhiteSpace($Server)) {
            $command.Server = $Server
        }

        foreach ($record in @(Resolve-DnsName @command)) {
            $addressProperty = $record.PSObject.Properties["IPAddress"]
            if ($null -eq $addressProperty) {
                continue
            }

            $address = [string]$addressProperty.Value
            if (-not (Test-PublicIPv4 $address) -or $found.Contains($address)) {
                continue
            }

            $found.Add($address) | Out-Null
        }
    }
    catch {
    }

    foreach ($address in $found) {
        Write-Output $address
    }
}

function Resolve-TunnelAddresses {
    param([string]$HostName)

    $delays = @(0, 3, 6, 12, 20, 30)
    $attempt = 0
    $flushed = $false
    foreach ($delay in $delays) {
        $attempt++
        if ($delay -gt 0) {
            Write-Info "El nombre del túnel todavía no resuelve. Reintento $attempt de $($delays.Count), espera ${delay}s."
            Start-Sleep -Seconds $delay
        }

        $local = New-Object System.Collections.Generic.List[string]
        foreach ($address in (Get-DnsAddresses -HostName $HostName)) {
            if (-not $local.Contains($address)) {
                $local.Add($address) | Out-Null
            }
        }

        $public = New-Object System.Collections.Generic.List[string]
        foreach ($server in @("1.1.1.1", "8.8.8.8")) {
            foreach ($address in (Get-DnsAddresses -HostName $HostName -Server $server)) {
                if (-not $public.Contains($address)) {
                    $public.Add($address) | Out-Null
                }
            }

            if ($public.Count -gt 0) {
                break
            }
        }

        if ($public.Count -gt 0) {
            if ($local.Count -eq 0 -and -not $flushed) {
                $flushed = $true
                try {
                    Clear-DnsClientCache
                }
                catch {
                }

                try {
                    & ipconfig.exe /flushdns | Out-Null
                }
                catch {
                }

                foreach ($address in (Get-DnsAddresses -HostName $HostName)) {
                    if (-not $local.Contains($address)) {
                        $local.Add($address) | Out-Null
                    }
                }

                if ($local.Count -eq 0) {
                    Write-WarnLine "El DNS del portátil no resuelve el túnel. 1.1.1.1 o 8.8.8.8 sí. La comprobación usa esas direcciones. PayPal consulta el DNS público, no el de esta red."
                }
            }

            $source = "publico"
            if ($local.Count -gt 0) {
                $source = "sistema"
            }

            return @{ Source = $source; Addresses = $public.ToArray() }
        }

        if ($local.Count -gt 0) {
            return @{ Source = "sistema"; Addresses = $local.ToArray() }
        }
    }

    return $null
}

function Get-CurlProbe {
    param(
        [string]$Url,
        [string[]]$Addresses
    )

    $curl = Join-Path $env:SystemRoot "System32\curl.exe"
    if (-not (Test-Path -LiteralPath $curl)) {
        $code = Get-HttpStatusCode -Url $Url -TimeoutMs 20000
        $exitCode = 0
        if ($code -eq 0) {
            $exitCode = 6
        }

        return @{ HttpCode = $code; ExitCode = $exitCode }
    }

    $uri = [Uri]$Url
    $arguments = @(
        "--silent", "--show-error", "--output", "NUL",
        "--write-out", "%{http_code}",
        "--max-time", "20",
        "--connect-timeout", "10",
        "--noproxy", "*"
    )
    foreach ($address in $Addresses) {
        if (-not (Test-PublicIPv4 $address)) {
            continue
        }

        $arguments += @("--resolve", "$($uri.Host):$($uri.Port):$address")
    }

    $arguments += $Url
    $result = Invoke-Native -File $curl -Arguments $arguments -WorkingDirectory $script:RepoRoot
    $code = 0
    [void][int]::TryParse(([string]$result.Stdout).Trim(), [ref]$code)
    return @{ HttpCode = $code; ExitCode = [int]$result.ExitCode }
}

function Test-PublicTunnel {
    param([string]$Origin)

    $webhookUrl = $Origin.TrimEnd("/") + $script:WebhookPath
    $adminUrl = $Origin.TrimEnd("/") + "/admin/login"
    $hostName = ([Uri]$Origin).Host
    $resolved = Resolve-TunnelAddresses -HostName $hostName
    if ($null -eq $resolved) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "dns"
            Status = 0
            ExitCode = 6
            DnsSource = ""
        }
    }

    $addresses = New-Object System.Collections.Generic.List[string]
    foreach ($address in @($resolved.Addresses)) {
        $text = [string]$address
        if ((Test-PublicIPv4 $text) -and -not $addresses.Contains($text)) {
            $addresses.Add($text) | Out-Null
        }
    }

    if ($addresses.Count -eq 0) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "dns"
            Status = 0
            ExitCode = 6
            DnsSource = [string]$resolved.Source
        }
    }

    $probeAddresses = $addresses.ToArray()
    $webhook = Get-CurlProbe -Url $webhookUrl -Addresses $probeAddresses
    $httpDelays = @(3, 6, 12, 20)
    foreach ($delay in $httpDelays) {
        if ($webhook.HttpCode -eq 405) {
            break
        }

        Write-Info "El webhook público todavía no devuelve 405 (HTTP $($webhook.HttpCode), curl $($webhook.ExitCode)). Reintento tras ${delay}s."
        Start-Sleep -Seconds $delay
        $webhook = Get-CurlProbe -Url $webhookUrl -Addresses $probeAddresses
    }

    if ($webhook.ExitCode -eq 6) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "dns"
            Status = 0
            ExitCode = 6
            DnsSource = [string]$resolved.Source
        }
    }

    if ($webhook.HttpCode -ne 405) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "http"
            Status = [int]$webhook.HttpCode
            ExitCode = [int]$webhook.ExitCode
            DnsSource = [string]$resolved.Source
        }
    }

    $admin = Get-CurlProbe -Url $adminUrl -Addresses $probeAddresses
    if ($admin.HttpCode -ne 404) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "admin"
            Status = [int]$admin.HttpCode
            ExitCode = [int]$admin.ExitCode
            DnsSource = [string]$resolved.Source
        }
    }

    $redsysUrl = $Origin.TrimEnd("/") + $script:RedsysPath
    $redsys = Get-CurlProbe -Url $redsysUrl -Addresses $probeAddresses
    if ($redsys.HttpCode -ne 405) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "redsys"
            Status = [int]$redsys.HttpCode
            ExitCode = [int]$redsys.ExitCode
            DnsSource = [string]$resolved.Source
        }
    }

    $paymentsUrl = $Origin.TrimEnd("/") + "/api/v1/checkout/payments"
    $payments = Get-CurlProbe -Url $paymentsUrl -Addresses $probeAddresses
    if ($payments.HttpCode -ne 404) {
        return @{
            Ok = $false
            WebhookUrl = $webhookUrl
            Reason = "privado"
            Status = [int]$payments.HttpCode
            ExitCode = [int]$payments.ExitCode
            DnsSource = [string]$resolved.Source
        }
    }

    return @{
        Ok = $true
        WebhookUrl = $webhookUrl
        Reason = ""
        Status = 405
        ExitCode = 0
        DnsSource = [string]$resolved.Source
    }
}

function Stop-RecordedCloudflare {
    param($State)

    $service = Get-ServiceState $State "cloudflared"
    if ($null -eq $service) {
        return
    }

    $supervisorPid = 0
    $rawSupervisor = Get-PropertyValue $service "supervisorPid"
    if ($null -ne $rawSupervisor) {
        [void][int]::TryParse([string]$rawSupervisor, [ref]$supervisorPid)
    }

    $childPid = 0
    $childPidFile = [string](Get-PropertyValue $service "childPidFile")
    if (-not [string]::IsNullOrWhiteSpace($childPidFile)) {
        $text = Read-SharedText $childPidFile
        [void][int]::TryParse($text.Trim(), [ref]$childPid)
    }

    if ($supervisorPid -gt 0) {
        $command = Get-ProcessCommandLine $supervisorPid
        if ($null -ne $command -and $command.IndexOf("dev-supervisor.ps1", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            & taskkill.exe /PID $supervisorPid /T /F | Out-Null
        }
    }

    if ($childPid -gt 0) {
        $command = Get-ProcessCommandLine $childPid
        if ($null -ne $command -and $command.IndexOf("127.0.0.1:5088", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            & taskkill.exe /PID $childPid /T /F | Out-Null
        }
    }

    $services = Get-PropertyValue $State "services"
    if ($null -ne $services -and $null -ne $services.PSObject.Properties["cloudflared"]) {
        [void]$services.PSObject.Properties.Remove("cloudflared")
    }

    Save-DevState $State
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $still = @(Get-CimInstance Win32_Process -Filter "Name = 'cloudflared.exe'" -ErrorAction SilentlyContinue | Where-Object {
            $_.CommandLine -like "*127.0.0.1:5088*"
        })
        if ($still.Count -eq 0) {
            break
        }

        Start-Sleep -Milliseconds 400
    }
}

function Update-RunningReport {
    if (Test-TcpPort -Port 5432) {
        $script:Report.Postgres = "OK"
    }

    if (Test-TcpPort -Port 8025) {
        $script:Report.Mailpit = "OK"
    }

    if ((Get-HttpStatusCode -Url "http://localhost:5141/swagger/index.html" -TimeoutMs 4000) -eq 200) {
        $script:Report.Api = "en marcha"
    }

    if (Test-TcpPort -Port 4200) {
        $script:Report.Angular = "en marcha"
    }

    if ((Get-HttpStatusCode -Url "http://127.0.0.1:5088$($script:WebhookPath)" -TimeoutMs 4000) -eq 405) {
        $script:Report.Relay = "en marcha"
    }
}

function Publish-TunnelSuccess {
    param($Check)

    $script:Report.Cloudflare = "en marcha"
    if ($Check.DnsSource -eq "publico") {
        $script:Report.Cloudflare = "en marcha, DNS público"
    }

    $script:Report.WebhookUrl = [string]$Check.WebhookUrl
    Write-Info "HTTP 405 confirma conectividad y que el relay rechaza GET."
    Write-Info "No confirma la firma del webhook ni registra la URL en PayPal."
    Write-Info "GET /admin/login y el checkout a través del túnel devuelven 404."
    Write-Info "GET de la notificación Redsys devuelve 405: la ruta existe y no acepta el navegador."
    Write-Info "La URL cambia cada vez que se crea el túnel. La registrada en PayPal tiene que ser esta:"
    Write-Info $script:Report.WebhookUrl
}

function Write-TunnelFailure {
    param($Check)

    $script:Report.Cloudflare = "error"
    if ($null -ne $Check -and -not [string]::IsNullOrWhiteSpace([string]$Check.WebhookUrl)) {
        $script:Report.WebhookUrl = [string]$Check.WebhookUrl
    }

    $retry = "No se ha registrado nada en PayPal. API, Angular y relay siguen en marcha. Reintenta solo el túnel con .\scripts\start-dev.ps1 -Only Cloudflare."
    if ($null -eq $Check) {
        Write-Fail "El túnel no ha registrado una conexión con una URL https://*.trycloudflare.com." $retry
        return
    }

    if ($Check.Reason -eq "admin" -or $Check.Reason -eq "privado") {
        Write-Fail "El túnel ha expuesto una ruta privada (HTTP $($Check.Status), $($Check.Reason)). Tiene que ser 404." "No se usa esa URL. API, Angular y relay siguen en marcha."
        return
    }

    if ($Check.Reason -eq "redsys") {
        Write-Fail "La notificación pública de Redsys no ha devuelto 405 (HTTP $($Check.Status))." "El túnel no se sustituye. Ejecuta .\scripts\start-dev.ps1 para renovar solo el relay y vuelve a comprobar Cloudflare."
        return
    }

    if ($Check.Reason -eq "dns") {
        Write-Fail "La URL del túnel actual no resuelve en el DNS del portátil ni en el DNS público." $retry
        return
    }

    Write-Fail "La URL pública no ha devuelto 405 (HTTP $($Check.Status))." $retry
}

function Start-DevCloudflare {
    param($State, [bool]$ReportStaleMerchant = $true)

    Write-Info ""
    Write-Info "[5/5] Cloudflare"
    if ($script:Report.Relay -notin @("en marcha", "ajeno")) {
        Write-Fail "No se abre el túnel porque el relay no está listo." "Cuando el relay responda 405, ejecuta .\scripts\start-dev.ps1 -Only Cloudflare. No se duplican los servicios que ya funcionan."
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($script:CloudflaredPath)) {
        Write-Fail "cloudflared no está instalado o no está en PATH." "Instálalo tú si quieres el túnel. El script no instala herramientas. API, Angular y relay pueden seguir en marcha."
        return $false
    }

    $restartsLeft = 1
    while ($true) {
        $origin = $null
        $stderrLog = $null
        $service = Get-ServiceState $State "cloudflared"
        if (Test-SupervisorAlive $service) {
            $stdoutLog = [string](Get-PropertyValue $service "stdoutLog")
            $stderrLog = [string](Get-PropertyValue $service "stderrLog")
            Write-Info "El túnel de este script sigue en marcha. Se usa solo la sesión registrada en su log actual."
            $origin = Wait-CurrentTunnelOrigin -StdoutLog $stdoutLog -StderrLog $stderrLog -TimeoutSec 20
        }
        else {
            $existing = @(Get-CimInstance Win32_Process -Filter "Name = 'cloudflared.exe'" -ErrorAction SilentlyContinue | Where-Object {
                $_.CommandLine -like "*127.0.0.1:5088*"
            })
            if ($existing.Count -gt 0) {
                Write-Fail "Ya hay un cloudflared apuntando al relay que este script no controla." "Detén ese proceso manualmente y ejecuta .\scripts\start-dev.ps1 -Only Cloudflare. No se reutiliza su URL temporal."
                return $false
            }

            Write-Info "Abriendo un túnel nuevo hacia http://127.0.0.1:5088."
            $started = Start-SupervisedProcess `
                -Name "cloudflared" `
                -FilePath $script:CloudflaredPath `
                -Arguments @("tunnel", "--url", "http://127.0.0.1:5088", "--no-autoupdate") `
                -WorkingDirectory $script:RepoRoot `
                -Environment @{} `
                -ChildCommandToken "127.0.0.1:5088"
            if ($null -eq $started) {
                Write-Fail "cloudflared no ha arrancado." "API, Angular y relay siguen disponibles. Revisa .\.dev\logs\cloudflared.err.log y ejecuta .\scripts\start-dev.ps1 -Only Cloudflare."
                return $false
            }

            Set-ServiceState $State "cloudflared" $started
            $stderrLog = [string]$started.stderrLog
            $origin = Wait-CurrentTunnelOrigin -StdoutLog $started.stdoutLog -StderrLog $started.stderrLog -TimeoutSec 45
        }

        if ([string]::IsNullOrWhiteSpace($origin)) {
            if ($restartsLeft -le 0) {
                Write-TunnelFailure $null
                if (-not [string]::IsNullOrWhiteSpace($stderrLog)) {
                    Get-SafeLogTail $stderrLog | ForEach-Object { Write-Host $_ }
                }

                return $false
            }

            $restartsLeft--
            Write-WarnLine "cloudflared no ha registrado una conexión nueva. Se reinicia solo ese proceso."
            Stop-RecordedCloudflare $State
            continue
        }

        Write-Info "Hay conexión registrada. La URL del log no se da por buena hasta que el nombre resuelva y el webhook responda."
        $check = Test-PublicTunnel -Origin $origin
        if ($check.Ok) {
            $script:TunnelOrigin = $origin
            Publish-TunnelSuccess $check
            if ($ReportStaleMerchant) {
                Watch-RedsysMerchantUrl -State $State -Origin $origin
            }

            return $true
        }

        if ($check.Reason -eq "admin" -or $check.Reason -eq "privado" -or $check.Reason -eq "redsys" -or $restartsLeft -le 0) {
            Write-TunnelFailure $check
            return $false
        }

        $restartsLeft--
        Write-WarnLine "El túnel registrado todavía no responde ($($check.Reason), HTTP $($check.Status), curl $($check.ExitCode)). Se reinicia solo cloudflared. API, Angular, relay y PostgreSQL no se tocan."
        Stop-RecordedCloudflare $State
    }
}

function Show-Summary {
    Write-Info ""
    Write-Info "Resumen"
    Write-Info "PostgreSQL: $($script:Report.Postgres)"
    Write-Info "Mailpit: $($script:Report.Mailpit)"
    Write-Info "API: $($script:Report.ApiUrl) [$($script:Report.Api)]"
    Write-Info "Administración: $($script:Report.AdminUrl)"
    Write-Info "Angular: $($script:Report.AngularUrl) [$($script:Report.Angular)]"
    Write-Info "Relay: $($script:Report.RelayUrl) [$($script:Report.Relay)]"
    Write-Info "Cloudflare: $($script:Report.Cloudflare)"
    Write-Info "PayPal: $($script:Report.PayPal)"
    Write-Info "Client ID: $($script:Report.ClientId)"
    Write-Info "Webhook ID: $($script:Report.WebhookId)"
    Write-Info "URL pública del webhook: $($script:Report.WebhookUrl)"
    Write-Info "Redsys: $($script:Report.Redsys)"
    Write-Info "MerchantUrl: $($script:Report.MerchantUrl)"
    Write-Info ""
    Write-Info "Logs:"
    Write-Info "  Get-Content -Wait .\.dev\logs\api.out.log"
    Write-Info "  Get-Content -Wait .\.dev\logs\angular.out.log"
    Write-Info "  Get-Content -Wait .\.dev\logs\relay.err.log"
    Write-Info "  Get-Content -Wait .\.dev\logs\cloudflared.err.log"
    Write-Info ""
    Write-Info "Para detener solo lo que ha creado este script:"
    Write-Info "  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\stop-dev.ps1"
    Write-Info "Para reintentar solo Cloudflare, sin credenciales y sin reiniciar el resto:"
    Write-Info "  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -Only Cloudflare"
    Write-Info "Para PayPal Sandbox y Redsys TEST, con la URL del túnel actual:"
    Write-Info "  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -WithRedsys"
    Write-Info "Si ese túnel cambia y la MerchantUrl queda vieja, solo se reinicia la API:"
    Write-Info "  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -Only Redsys"
    Write-Info "El checkout de Development sigue limitado a un Administrator autenticado."
    if ($script:Report.Redsys -eq "TEST") {
        Write-Info "Redsys queda en TEST. No se ha enviado ningún pago a la pasarela."
    }
    else {
        Write-Info "Stripe no se activa. Redsys permanece desactivado en este arranque. No se crean pedidos ni pagos."
    }

    if ($script:Failures.Count -gt 0) {
        Write-Info ""
        Write-Info "Incidencias:"
        $script:Failures | Select-Object -Unique | ForEach-Object { Write-Info "  - $_" }
    }
}

$mutex = New-Object System.Threading.Mutex($false, "Local\DominioDeLaSierraDevStart")
$mutexOwned = $false
try {
    try {
        $mutexOwned = $mutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        $mutexOwned = $true
    }

    if (-not $mutexOwned) {
        Write-Host "ERROR: Ya hay otro arranque de start-dev.ps1 en curso."
        exit 1
    }

    $script:RepoRoot = Get-RepoRoot
    $script:DevRoot = Join-Path $script:RepoRoot ".dev"
    $script:LogRoot = Join-Path $script:DevRoot "logs"
    $script:StatePath = Join-Path $script:DevRoot "state.json"
    $script:SupervisorPath = Join-Path $PSScriptRoot "dev-supervisor.ps1"
    New-Item -ItemType Directory -Force -Path $script:DevRoot | Out-Null

    Write-Info "Dominio de la Sierra — arranque local"
    Write-Info "Raíz: $($script:RepoRoot)"

    $missing = New-Object System.Collections.Generic.List[string]
    $script:DotNetPath = Resolve-RequiredCommand "dotnet"
    $script:NodePath = Resolve-RequiredCommand "node"
    $npmPath = Resolve-RequiredCommand "npm"
    $dockerPath = Resolve-RequiredCommand "docker"
    $script:CloudflaredPath = Resolve-CloudflaredPath
    if ($null -eq $script:DotNetPath) { $missing.Add("dotnet") | Out-Null }
    if ($null -eq $script:NodePath) { $missing.Add("node") | Out-Null }
    if ($null -eq $npmPath) { $missing.Add("npm") | Out-Null }
    if ($null -eq $dockerPath) { $missing.Add("docker") | Out-Null }
    if ($null -eq $script:CloudflaredPath) {
        Write-WarnLine "cloudflared no está disponible. El túnel no se abrirá; el script no instala herramientas."
    }
    if ($Only -and $WithRedsys) {
        Write-Fail "Elige un solo modo: -Only Cloudflare, -Only Redsys o -WithRedsys." "No se reinicia ningún servicio."
        Show-Summary
        exit 1
    }

    if ($Only -eq "Cloudflare") {
        Write-Info "Reintento solo de Cloudflare. No se piden credenciales ni se reinician API, Angular, relay ni PostgreSQL."
        if ([string]::IsNullOrWhiteSpace($script:CloudflaredPath)) {
            Write-Fail "cloudflared no está instalado o no está en PATH." "Instálalo tú. Este script no instala herramientas. El resto de servicios no se toca."
            Show-Summary
            exit 1
        }

        $state = Read-DevState
        Update-RunningReport
        $script:Report.ClientId = "no se vuelve a pedir"
        $script:Report.WebhookId = "sin cambios"
        if ($script:Report.Relay -ne "en marcha") {
            Write-Fail "El relay no responde 405 en 127.0.0.1:5088." "El túnel no se abre. Cuando el relay esté en marcha, ejecuta .\scripts\start-dev.ps1 -Only Cloudflare."
            Show-Summary
            exit 1
        }

        Start-DevCloudflare $state | Out-Null
        Show-Summary
        if ($script:Failures.Count -gt 0) {
            exit 1
        }

        exit 0
    }

    if ($Only -eq "Redsys") {
        Write-Info "Recuperación de Redsys TEST. Solo se toca la API, y el relay si su filtro no es el actual."
        if ([string]::IsNullOrWhiteSpace($script:CloudflaredPath) -or $null -eq $script:DotNetPath) {
            Write-Fail "Hacen falta dotnet y cloudflared." "No se reinicia la API."
            Show-Summary
            exit 1
        }

        if (-not (Test-DotNet10Sdk)) {
            Write-Fail "No hay un SDK de .NET 10." "No se reinicia la API."
            Show-Summary
            exit 1
        }

        $dockerInfo = Invoke-Native -File "docker" -Arguments @("info") -WorkingDirectory $script:RepoRoot
        if ($dockerInfo.ExitCode -ne 0) {
            Write-Fail "Docker Desktop no está disponible." "No se reinicia la API."
            Show-Summary
            exit 1
        }

        $state = Read-DevState
        if (-not (Test-TcpPort -Port 5432)) {
            if (-not (Start-DevDocker $state)) {
                Show-Summary
                exit 1
            }
        }
        else {
            $script:Report.Postgres = "OK"
            if (Test-TcpPort -Port 8025) {
                $script:Report.Mailpit = "OK"
            }
        }

        if (-not (Start-DevRelay $state)) {
            Write-Fail "Sin el filtro actual del relay no se actualiza Redsys." "La API sigue como estaba."
            Show-Summary
            exit 1
        }

        if (-not (Start-DevCloudflare -State $state -ReportStaleMerchant $false)) {
            Write-Fail "Sin el túnel actual no se cambia la API." "PayPal, Angular y PostgreSQL siguen como estaban."
            Show-Summary
            exit 1
        }

        $merchantUrl = New-RedsysMerchantUrl $script:TunnelOrigin
        $paypal = Read-PayPalCredentials
        $redsys = $null
        if ($null -ne $paypal -and -not [string]::IsNullOrWhiteSpace($merchantUrl)) {
            $redsys = Read-RedsysCredentials -MerchantUrl $merchantUrl
        }

        $apiReady = $false
        if ($null -ne $paypal -and $null -ne $redsys) {
            $apiReady = Start-DevApi -State $state -Credentials $paypal -Redsys $redsys -RestartOwned -Step "[API] Redsys TEST"
            if ($apiReady) {
                Set-RedsysLaunchState -State $state -Enabled $true -Origin $script:TunnelOrigin
            }
        }

        Clear-CredentialBag $paypal
        Clear-CredentialBag $redsys
        if (-not $apiReady) {
            Update-RunningReport
            if ($script:Report.Api -eq "en marcha") {
                $script:Report.ClientId = "configurado en el proceso de la API"
                $script:Report.WebhookId = "configurado"
                Write-Info "La API que ya estaba en marcha no se ha reiniciado."
            }
        }

        Show-Summary
        if (-not $apiReady -or $script:Failures.Count -gt 0) {
            exit 1
        }

        exit 0
    }

    if ($missing.Count -gt 0) {
        Write-Fail ("Faltan herramientas: " + ($missing -join ", ") + ".") "Instálalas tú. Este script no instala nada."
        if ($missing -contains "dotnet" -or $missing -contains "docker") {
            Show-Summary
            exit 1
        }
    }

    if ($null -ne $script:DotNetPath -and -not (Test-DotNet10Sdk)) {
        Write-Fail "No hay un SDK de .NET 10." "El proyecto lo necesita. El script no lo instala."
        Show-Summary
        exit 1
    }

    $dockerInfo = Invoke-Native -File "docker" -Arguments @("info") -WorkingDirectory $script:RepoRoot
    if ($dockerInfo.ExitCode -ne 0) {
        Write-Fail "Docker Desktop no está disponible." "Ábrelo y vuelve a ejecutar .\scripts\start-dev.ps1."
        Show-Summary
        exit 1
    }

    $state = Read-DevState
    $dockerReady = Start-DevDocker $state
    if ($WithRedsys) {
        if (-not $dockerReady) {
            Write-Fail "Sin PostgreSQL no se habilita Redsys." "Corrige Docker y vuelve a ejecutar .\scripts\start-dev.ps1 -WithRedsys."
            Show-Summary
            exit 1
        }

        if ($null -ne $script:NodePath) {
            Start-DevAngular $state | Out-Null
        }

        $relayReady = $false
        if ($null -ne $script:DotNetPath) {
            $relayReady = Start-DevRelay $state
        }

        $tunnelReady = $false
        if ($relayReady) {
            $tunnelReady = Start-DevCloudflare -State $state -ReportStaleMerchant $false
        }

        $paypal = $null
        $redsys = $null
        $apiReady = $false
        if ($tunnelReady) {
            $merchantUrl = New-RedsysMerchantUrl $script:TunnelOrigin
            $listening = Test-TcpPort -Port 5141
            $ownedApi = Test-SupervisorAlive (Get-ServiceState $state "api")
            if ($listening -and -not $ownedApi) {
                Write-Fail "El puerto 5141 está ocupado por una API ajena." "No se cambia su entorno ni se habilita Redsys."
            }
            elseif ([string]::IsNullOrWhiteSpace($merchantUrl)) {
                Write-Fail "El túnel actual no ha dado una URL válida para Redsys." "La API no se reinicia."
            }
            else {
                $paypal = Read-PayPalCredentials
                if ($null -ne $paypal) {
                    $redsys = Read-RedsysCredentials -MerchantUrl $merchantUrl
                }

                if ($null -ne $paypal -and $null -ne $redsys) {
                    $apiReady = Start-DevApi -State $state -Credentials $paypal -Redsys $redsys -RestartOwned:$listening -Step "[5/5] API"
                    if ($apiReady) {
                        Set-RedsysLaunchState -State $state -Enabled $true -Origin $script:TunnelOrigin
                    }
                }
            }
        }
        else {
            Write-Fail "Sin el túnel actual no se habilita Redsys." "Si la API ya estaba en marcha, no se reinicia."
        }

        Clear-CredentialBag $paypal
        Clear-CredentialBag $redsys
        if (-not $apiReady) {
            Update-RunningReport
            if ($script:Report.Api -eq "en marcha") {
                $script:Report.ClientId = "configurado en el proceso de la API"
                $script:Report.WebhookId = "configurado"
                Write-Info "La API que ya estaba en marcha no se ha reiniciado."
            }
        }

        Show-Summary
        if (-not $apiReady -or $script:Failures.Count -gt 0) {
            exit 1
        }

        exit 0
    }

    $credentials = $null
    $apiListening = Test-TcpPort -Port 5141
    if ($dockerReady -and -not $apiListening) {
        $credentials = Read-PayPalCredentials
    }
    elseif ($dockerReady -and $apiListening) {
        $ownedApi = Test-SupervisorAlive (Get-ServiceState $state "api")
        if ($ownedApi) {
            $script:Report.ClientId = "configurado en el proceso de la API"
            $script:Report.WebhookId = "configurado"
        }
    }

    $apiReady = $false
    if ($dockerReady) {
        $apiReady = Start-DevApi $state $credentials
    }
    else {
        Write-Fail "Sin PostgreSQL no se arranca la API." "Corrige Docker y vuelve a ejecutar .\scripts\start-dev.ps1."
    }

    Clear-CredentialBag $credentials

    if ($null -ne $script:NodePath) {
        Start-DevAngular $state | Out-Null
    }

    if ($null -ne $script:DotNetPath) {
        Start-DevRelay $state | Out-Null
    }

    Start-DevCloudflare $state | Out-Null

    Show-Summary
    if ($script:Failures.Count -gt 0) {
        exit 1
    }

    exit 0
}
catch {
    $message = [string]$_.Exception.Message
    foreach ($value in @($script:SensitiveValues)) {
        if (-not [string]::IsNullOrEmpty($value) -and $message.Contains($value)) {
            $message = "Se ha producido un error y el detalle se ha omitido para no mostrar credenciales."
            break
        }
    }

    Write-Host "ERROR: $message"
    exit 1
}
finally {
    Clear-SensitiveValues
    if ($mutexOwned) {
        $mutex.ReleaseMutex() | Out-Null
    }

    $mutex.Dispose()
}
