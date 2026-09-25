<#
.SYNOPSIS
    Opens one help patch in vvvv with the package repositories it needs, so it can be read,
    tested and laid out by hand.

.DESCRIPTION
    NEVER type the launch by hand: vvvv IGNORES a repository folder that does not exist and
    the failure surfaces as an error naming something else. This script passes dist\ (the
    staged package, from build.ps1) and deps\ (NetTopologySuite and its dependencies), and
    refuses to launch when either is missing. When the vvvv THIS launcher started is still
    running, the patch is opened as another tab in it (vvvv is single-instance and forwards the
    file); a vvvv somebody else started is never touched.

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

$pidFile = Join-Path $env:TEMP 'vl-nettopologysuite-vvvv.pid'

# ALREADY RUNNING: open the patch as a new tab in OUR vvvv instead of refusing (2026-09-25,
# carried from vl-mapsui, which measured it). vvvv gamma is single-instance unless started with
# -m: a second `vvvv.exe <file>` hands the file to the running instance and exits within a second.
# The running instance already has the package repositories, so none are passed. Only when the
# running vvvv is the one this launcher started (its pid is in the pid file) and it is the only
# one: a sibling session on this machine runs its own vvvv, and a file must never be pushed into
# their window - nor could we say which of two instances would receive it. Never kill either.
$running = @(Get-Process vvvv -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $ours = $null
    if (Test-Path $pidFile) { $ours = [int](Get-Content $pidFile -ErrorAction SilentlyContinue) }
    if ($running.Count -eq 1 -and $running[0].Id -eq $ours) {
        Write-Host "`nopening $(Split-Path $target -Leaf) as a new tab in the running vvvv (pid $ours)"
        Start-Process -FilePath $vvvv -ArgumentList @("`"$target`"") | Out-Null
        # If forwarding ever stops working a second vvvv stays up - say so rather than leaving two
        # instances holding the same staged assemblies.
        Start-Sleep -Seconds 3
        $now = @(Get-Process vvvv -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $ours })
        if ($now) {
            Write-Host "a SECOND vvvv started (pid $($now.Id -join ', ')) instead of a new tab - close it; forwarding did not work" -ForegroundColor Red
            exit 1
        }
        Write-Host "READ IT, ADJUST IT, SAVE IT. Every open tab is running.`n" -ForegroundColor Yellow
        exit 0
    }
    Write-Host "`na vvvv is running that this launcher did not start (pid $($running.Id -join ', '))." -ForegroundColor Red
    Write-Host "It may belong to another session on this machine - not opening anything into it." -ForegroundColor Red
    Write-Host "Close it (if it is yours) and try again.`n" -ForegroundColor Red
    exit 1
}

Write-Host "`nopening $(Split-Path $target -Leaf)"
$repos | ForEach-Object { Write-Host "  repo  $_" }
Write-Host ''

$vvvvArgs = @("`"$target`"", '--package-repositories', "`"$($repos -join ';')`"")
if ($Log) { $vvvvArgs += '--log'; Write-Host ('  logging to ' + $env:USERPROFILE + '\Documents\vvvv\gamma\vvvv_<timestamp>.log') }
$proc = Start-Process -FilePath $vvvv -ArgumentList $vvvvArgs -PassThru
# Sibling repositories launch vvvv from their own sessions. The rule agreed 2026-09-24 after one
# session killed another's window: each launcher records its pid here, stops only that pid, and
# waits instead of launching while a vvvv it did not start is running.
Set-Content $pidFile $proc.Id
Write-Host "vvvv pid $($proc.Id) (written to $pidFile - close that pid, never every vvvv)" -ForegroundColor DarkGray

Write-Host "READ IT, ADJUST IT, SAVE IT. Opening a document in vvvv is running it; the next launch" -ForegroundColor Yellow
Write-Host "  opens as another tab in this same vvvv. When done: close vvvv, then" -ForegroundColor Yellow
Write-Host "  .\tools\Normalize-HelpPatches.ps1 ; .\tools\Test-VLPatch.ps1`n" -ForegroundColor Yellow
exit 0
