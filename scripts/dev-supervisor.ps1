#requires -Version 5.1
<#
  Proceso interno usado por scripts/start-dev.ps1.
  Recibe una única línea Base64 por la entrada estándar y no la escribe en disco.
  Mantiene vivo el proceso hijo y copia su salida a los logs indicados.
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Format-Arguments {
    param([string[]]$Arguments)

    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            continue
        }

        $text = [string]$argument
        if ($text -match '[\s"]') {
            $parts.Add('"' + ($text -replace '"', '\"') + '"')
        }
        else {
            $parts.Add($text)
        }
    }

    return ($parts -join " ")
}

function Start-OutputPump {
    param($Reader, $Writer)

    $shell = [PowerShell]::Create()
    [void]$shell.AddScript({
        param($Reader, $Writer)
        try {
            while ($null -ne ($line = $Reader.ReadLine())) {
                $Writer.WriteLine($line)
            }
        }
        catch {
        }
    })
    [void]$shell.AddArgument($Reader)
    [void]$shell.AddArgument($Writer)
    return @{
        Shell = $shell
        Handle = $shell.BeginInvoke()
    }
}

function Write-SafeFailure {
    param($Writer, [string]$Message)

    $allowed = @(
        "El supervisor no ha recibido la orden de arranque.",
        "La orden de arranque no es válida.",
        "No se encuentra el ejecutable.",
        "No se ha podido crear el proceso."
    )

    $line = "No se ha podido arrancar el proceso."
    if ($allowed -contains $Message) {
        $line = $Message
    }

    if ($null -ne $Writer) {
        $Writer.WriteLine($line)
    }
}

$stdoutWriter = $null
$stderrWriter = $null

try {
    $encoded = [Console]::In.ReadLine()
    if ([string]::IsNullOrWhiteSpace($encoded)) {
        throw "El supervisor no ha recibido la orden de arranque."
    }

    $payload = $null
    try {
        $payloadText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded.Trim()))
        $payload = $payloadText | ConvertFrom-Json
    }
    catch {
        throw "La orden de arranque no es válida."
    }
    finally {
        $encoded = $null
        $payloadText = $null
    }

    $stdoutLog = [string]$payload.stdoutLog
    $stderrLog = [string]$payload.stderrLog
    $workingDirectory = [string]$payload.workingDirectory
    $childPidFile = [string]$payload.childPidFile
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $stdoutLog) | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $childPidFile) | Out-Null

    $utf8 = New-Object System.Text.UTF8Encoding $false
    $stdoutStream = New-Object System.IO.FileStream(
        $stdoutLog,
        [IO.FileMode]::Append,
        [IO.FileAccess]::Write,
        [IO.FileShare]::ReadWrite)
    $stderrStream = New-Object System.IO.FileStream(
        $stderrLog,
        [IO.FileMode]::Append,
        [IO.FileAccess]::Write,
        [IO.FileShare]::ReadWrite)
    $stdoutWriter = New-Object System.IO.StreamWriter($stdoutStream, $utf8)
    $stderrWriter = New-Object System.IO.StreamWriter($stderrStream, $utf8)
    $stdoutWriter.AutoFlush = $true
    $stderrWriter.AutoFlush = $true
    $script:stdoutWriter = $stdoutWriter
    $script:stderrWriter = $stderrWriter

    $stdoutWriter.WriteLine("---- arranque $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK') ----")

    $filePath = [string]$payload.filePath
    if (-not (Test-Path -LiteralPath $filePath)) {
        throw "No se encuentra el ejecutable."
    }

    $arguments = @()
    foreach ($item in @($payload.arguments)) {
        if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace([string]$item)) {
            $arguments += [string]$item
        }
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $filePath
    $startInfo.Arguments = (Format-Arguments $arguments)
    $startInfo.WorkingDirectory = $workingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    if ($null -ne $payload.PSObject.Properties["remove"]) {
        foreach ($name in @($payload.remove)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$name)) {
                [void]$startInfo.EnvironmentVariables.Remove([string]$name)
            }
        }
    }

    if ($null -ne $payload.PSObject.Properties["set"] -and $null -ne $payload.set) {
        foreach ($property in $payload.set.PSObject.Properties) {
            $startInfo.EnvironmentVariables[[string]$property.Name] = [string]$property.Value
        }
    }

    $stdoutWriter.WriteLine("comando: $filePath $($startInfo.Arguments)")

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo

    if (-not $process.Start()) {
        throw "No se ha podido crear el proceso."
    }

    $stdoutPump = Start-OutputPump $process.StandardOutput $stdoutWriter
    $stderrPump = Start-OutputPump $process.StandardError $stderrWriter
    Set-Content -LiteralPath $childPidFile -Value $process.Id -Encoding ASCII

    $payload = $null
    $startInfo = $null
    $process.WaitForExit()
    [void]$stdoutPump.Shell.EndInvoke($stdoutPump.Handle)
    [void]$stderrPump.Shell.EndInvoke($stderrPump.Handle)
    $stdoutPump.Shell.Dispose()
    $stderrPump.Shell.Dispose()
    $stdoutWriter.WriteLine("---- fin $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK') codigo $($process.ExitCode) ----")
    exit $process.ExitCode
}
catch {
    Write-SafeFailure $stderrWriter $_.Exception.Message
    exit 1
}
finally {
    if ($null -ne $stdoutWriter) {
        $stdoutWriter.Dispose()
    }

    if ($null -ne $stderrWriter) {
        $stderrWriter.Dispose()
    }
}
