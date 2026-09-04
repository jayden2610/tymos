#Requires -Version 5.1
<#
.SYNOPSIS
  One-command local run: Tymos web + WinUI floating pill.

.EXAMPLE
  .\pill\run-windows.ps1

.EXAMPLE
  .\pill\run-windows.ps1 -Demo
#>
param(
  [switch]$Demo,
  [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $RepoRoot 'index.html'))) {
  $RepoRoot = $PSScriptRoot
  if (-not (Test-Path (Join-Path $RepoRoot 'index.html'))) {
    throw 'Run this from the tymos repo (pill\run-windows.ps1).'
  }
}

function Assert-DotNet {
  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) {
    Write-Host @'
.NET SDK not found.

Install with:
  winget install Microsoft.DotNet.SDK.8

Then close and reopen this terminal, and run:
  .\pill\run-windows.ps1
'@ -ForegroundColor Yellow
    exit 1
  }
  Write-Host "dotnet: $(dotnet --version)"
}

function Start-WebServer {
  $listening = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
  if ($listening) {
    Write-Host 'Web already on http://localhost:8080'
    return
  }
  $python = Get-Command python -ErrorAction SilentlyContinue
  if (-not $python) { $python = Get-Command python3 -ErrorAction SilentlyContinue }
  if (-not $python) {
    Write-Host 'Python not found. Start the web app yourself with any static server on port 8080.' -ForegroundColor Yellow
    return
  }
  Write-Host 'Starting web server on http://localhost:8080 ...'
  Start-Process -FilePath $python.Source -ArgumentList @('-m', 'http.server', '8080') -WorkingDirectory $RepoRoot -WindowStyle Normal
  Start-Sleep -Seconds 1
}

Assert-DotNet

if (-not $Demo) {
  Start-WebServer
}

$proj = Join-Path $RepoRoot 'pill\winui\TymosPill\TymosPill.csproj'
Write-Host 'Restoring and building TymosPill ...'
Push-Location (Split-Path $proj)
try {
  dotnet restore $proj
  if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

  $runArgs = @('run', '-c', 'Release', '--project', $proj, '-r', 'win-x64', '--no-launch-profile')
  if ($Demo) {
    Write-Host 'Launching pill in --demo mode (sample state, no bridge needed).'
    $runArgs += @('--', '--demo')
  } else {
    Write-Host 'Launching pill (bridge on http://127.0.0.1:17865).'
  }

  if (-not $SkipBrowser -and -not $Demo) {
    Start-Process 'http://localhost:8080/'
  }

  Write-Host ''
  Write-Host 'When the pill window is open: focus a task in Tymos, press Start.' -ForegroundColor Cyan
  Write-Host 'Close the pill window to stop this script.' -ForegroundColor Cyan
  Write-Host ''
  & dotnet @runArgs
  if ($LASTEXITCODE -ne 0) { throw 'dotnet run failed' }
}
finally {
  Pop-Location
}
