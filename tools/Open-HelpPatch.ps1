<#
.SYNOPSIS
    Opens one help patch in vvvv with the package repositories it needs, so it can be read,
    tested and laid out by hand.

.DESCRIPTION
    NEVER type the launch by hand: vvvv IGNORES a repository folder that does not exist and
    the failure surfaces as an error naming something else. This script passes dist\ (the
    staged package, from build.ps1) and deps\ (NetTopologySuite and its dependencies), and
    refuses to launch when either is missing or when vvvv is already running.

    OPENING A DOCUMENT IN VVVV IS RUNNING IT. Read it, adjust it, save it, close vvvv. Then run
    tools\Normalize-HelpPatches.ps1: opening a patch rewrites its NugetDependency version to
    whatever is installed, and saving keeps it. Then tools\Test-VLPatch.ps1, which checks the
    layout arithmetic a hand edit may have broken (a Pad's Comment renders to the RIGHT of the
    box, 6.5 px per character).

    Carried from vl-overworld\tools\Open-HelpPatch.ps1, minus its six repositories.

.EXAMPLE
    .\tools\Open-HelpPatch.ps1 "Buffer"
    .\tools\Open-HelpPatch.ps1 -List
    .\tools\Open-HelpPatch.ps1 -Path .\scratch\probe.vl
#>
param(
    [Parameter(Position = 0)]
    [string]$Patch,

    [switch]$List,

    # Also pass --log: vvvv writes Documents\vvvv\gamma\vvvv_<timestamp>.log, the only place a
    # runtime exception inside a node shows up as text.
    [switch]$Log,

    # Any .vl, help patch or not.
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$HelpDir  = Join-Path $RepoRoot 'help\VL.NetTopologySuite'
$patches  = @(Get-ChildItem $HelpDir -File -Filter *.vl | Sort-Object Name)

if ($List -or (-not $Patch -and -not $Path)) {
    Write-Host "`nhelp patches in $HelpDir`n"
    $patches | ForEach-Object { Write-Host "  $($_.BaseName)" }
    Write-Host "`nusage: .\tools\Open-HelpPatch.ps1 ""Buffer""`n"
    exit 0
}

if ($Path) {
    if (-not (Test-Path $Path)) { Write-Host "no such file: $Path" -ForegroundColor Red; exit 1 }
    $target = (Resolve-Path $Path).Path
} else {
    # Exactly one match or nothing - a launch that opens the wrong patch wastes a round.
    $hits = @($patches | Where-Object { $_.BaseName -like "*$Patch*" })
    if ($hits.Count -eq 0) { Write-Host "no help patch matches '$Patch'. -List shows them." -ForegroundColor Red; exit 1 }
    if ($hits.Count -gt 1) {
        Write-Host "'$Patch' matches $($hits.Count) patches:" -ForegroundColor Red
        $hits | ForEach-Object { Write-Host "  $($_.BaseName)" }
        exit 1
    }
    $target = $hits[0].FullName
}

$vvvv = & (Join-Path $PSScriptRoot 'Find-Vvvv.ps1')
if (-not (Test-Path $vvvv)) { Write-Host "vvvv not found at $vvvv" -ForegroundColor Red; exit 1 }

$repos = @((Join-Path $RepoRoot 'dist'), (Join-Path $RepoRoot 'deps'))
$missing = @($repos | Where-Object { -not (Test-Path $_) })
if ($missing.Count -gt 0) {
    Write-Host "`nmissing package repositories - vvvv would report this as a missing PACKAGE, or as nodes with no pins:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "  run .\build.ps1 first`n" -ForegroundColor Red
    exit 1
}
if (-not (Get-ChildItem (Join-Path $RepoRoot 'dist') -Directory -ErrorAction SilentlyContinue)) {
    Write-Host "`ndist\ is empty - run .\build.ps1 first`n" -ForegroundColor Red
    exit 1
}

# Detect and refuse - never kill. A running vvvv holds the staged assemblies open.
$running = @(Get-Process vvvv -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "`nvvvv is ALREADY RUNNING (PID $($running.Id -join ', ')). Close it first.`n" -ForegroundColor Red
    exit 1
}

Write-Host "`nopening $(Split-Path $target -Leaf)"
$repos | ForEach-Object { Write-Host "  repo  $_" }
Write-Host ''

$vvvvArgs = @("`"$target`"", '--package-repositories', "`"$($repos -join ';')`"")
if ($Log) { $vvvvArgs += '--log'; Write-Host ('  logging to ' + $env:USERPROFILE + '\Documents\vvvv\gamma\vvvv_<timestamp>.log') }
Start-Process -FilePath $vvvv -ArgumentList $vvvvArgs

Write-Host "READ IT, ADJUST IT, SAVE IT, CLOSE VVVV. Opening a document in vvvv is running it." -ForegroundColor Yellow
Write-Host "  afterwards: .\tools\Normalize-HelpPatches.ps1 ; .\tools\Test-VLPatch.ps1`n" -ForegroundColor Yellow
exit 0
