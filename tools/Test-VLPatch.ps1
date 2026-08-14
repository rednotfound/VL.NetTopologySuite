<#
.SYNOPSIS
    Validates .vl patch documents structurally, without needing vvvv.

.DESCRIPTION
    Test-VLPackage.ps1 checks the package: that the entry point exists, forwards an assembly with
    [ImportAsIs], and declares its nugets. It does not look inside a help patch. This does.

    Every check here corresponds to a failure that is SILENT in vvvv - the patch opens, nothing is
    red, and something simply does not work:

      BOM              a .vl without a UTF-8 BOM is not loaded at all
      ID format        22 chars, first [A-V] - vvvv's deserializer rejects anything else quietly
      ID uniqueness    duplicates make one node shadow another
      link endpoints   a Link naming an ID that is not a Pin or Pad is dropped without warning
      dangling IOBox   a Pad connected to nothing is decoration the user cannot use
      empty Path IOBox Value="" on a Path means the DOCUMENT'S OWN FOLDER, not "empty" - this is
                       the one that wrote 444 files next to two repositories with every guard
                       reporting success

    It cannot tell you whether a node resolves: that depends on what the assembly actually exports,
    and only the GUI proves a node appears under the expected category. A PASS here means the
    document is well formed, not that it works.

.EXAMPLE
    .\tools\Test-VLPatch.ps1
    .\tools\Test-VLPatch.ps1 -Path 'help\VL.NetTopologySuite\01 Create a Point.vl'
#>
param(
    [string]$Path,
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$targets = if ($Path) {
    @(Get-Item (Join-Path $RepoRoot $Path) -ErrorAction Stop)
} else {
    @(Get-ChildItem $RepoRoot -Filter '*.vl' -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\dist\\' } | Sort-Object FullName)
}
if ($targets.Count -eq 0) { throw "No .vl documents found under $RepoRoot" }

$totalProblems = 0

foreach ($file in $targets) {
    $rel = $file.FullName.Replace("$RepoRoot\", '')
    Write-Host "`nvalidating $rel" -ForegroundColor Cyan

    $problems = [System.Collections.Generic.List[string]]::new()
    $raw   = Get-Content $file.FullName -Raw
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    if ($bytes.Length -lt 3 -or -not (($bytes[0] -eq 0xEF) -and ($bytes[1] -eq 0xBB) -and ($bytes[2] -eq 0xBF))) {
        $problems.Add('missing UTF-8 BOM - vvvv will not load this document')
    }
    try { $null = [xml]$raw } catch { $problems.Add("XML does not parse: $($_.Exception.Message)") }

    $allIds = @(([regex]'Id="([^"]+)"').Matches($raw) | ForEach-Object { $_.Groups[1].Value })
    $illegal = @($allIds | Where-Object { $_ -notmatch '^[A-V][0-9A-Za-z]{21}$' })
    if ($illegal.Count) { $problems.Add("illegal IDs: $($illegal -join ', ')") }
    $dupes = @($allIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($dupes.Count) { $problems.Add("duplicate IDs: $($dupes -join ', ')") }

    # Pins and Pads are the only things a Link may join.
    $endpoints = @(
        ([regex]'<Pin Id="([^"]+)"').Matches($raw)  | ForEach-Object { $_.Groups[1].Value }
        ([regex]'<Pad Id="([^"]+)"').Matches($raw)  | ForEach-Object { $_.Groups[1].Value }
    )
    $linkMatches = ([regex]'<Link Id="[^"]+" Ids="([^,]+),([^"]+)"').Matches($raw)
    foreach ($m in $linkMatches) {
        foreach ($e in @($m.Groups[1].Value, $m.Groups[2].Value)) {
            if ($endpoints -notcontains $e) { $problems.Add("link endpoint $e is neither a Pin nor a Pad") }
        }
    }

    $linked = @($linkMatches | ForEach-Object { $_.Groups[1].Value; $_.Groups[2].Value })
    foreach ($pad in ([regex]'<Pad Id="([^"]+)"').Matches($raw) | ForEach-Object { $_.Groups[1].Value }) {
        if ($linked -notcontains $pad) { $problems.Add("dangling Pad $pad - an IOBox wired to nothing") }
    }

    # An empty Path IOBox is NOT empty: Value="" means the path relative to this document, which is
    # the document's own folder, absolute. Only an unconnected pin can mean "the default".
    $pathPadRegex = [regex]::new(
        '<Pad Id="([^"]+)"[^>]*Value=""[^>]*>(?<body>.*?)</Pad>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($m in $pathPadRegex.Matches($raw)) {
        if ($m.Groups['body'].Value -match 'Name="Path"') {
            $problems.Add("Pad $($m.Groups[1].Value) is an empty Path IOBox - this resolves to the document's own folder, not to nothing")
        }
    }

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "  FAIL  $_" -ForegroundColor Red }
        $totalProblems += $problems.Count
    } else {
        Write-Host "  ok    UTF-8 BOM" -ForegroundColor DarkGray
        Write-Host "  ok    $($allIds.Count) IDs, well formed and unique" -ForegroundColor DarkGray
        Write-Host "  ok    $($linkMatches.Count) links, all endpoints resolve" -ForegroundColor DarkGray
        Write-Host "  ok    no dangling IOBoxes" -ForegroundColor DarkGray
    }
}

Write-Host ''
if ($totalProblems -gt 0) {
    Write-Host "FAIL - $totalProblems problem(s) across $($targets.Count) document(s)." -ForegroundColor Red
    exit 1
}
Write-Host "PASS - $($targets.Count) document(s) structurally valid." -ForegroundColor Green
Write-Host "Note: this proves the documents are well formed. Only the GUI proves a node resolves." -ForegroundColor Yellow
