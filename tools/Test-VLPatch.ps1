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

# [array] on the assignment, not just @() inside the branches: PowerShell unwraps a
# single-element array back to a scalar on its way out of an `if` expression, and under
# Set-StrictMode the next line then fails with "The property 'Count' cannot be found".
# Caught by running this with -Path on one file, which is the case @() alone does not cover.
[array]$targets = if ($Path) {
    Get-Item (Join-Path $RepoRoot $Path) -ErrorAction Stop
} else {
    Get-ChildItem $RepoRoot -Filter '*.vl' -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\dist\\' } | Sort-Object FullName
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

# ── Help.xml must account for every patch, and name only patches that exist ──
# Both directions fail silently: a patch missing from Help.xml still ships, but unordered and
# untagged, and a link naming a file that is not there just lists nothing. Numbering the files
# instead of ordering them here is what produced "01 03 04 06", where every gap read as a broken
# install - so this is now the only place ordering lives, and it has to be checked.
if (-not $Path) {
    foreach ($helpXml in Get-ChildItem $RepoRoot -Filter 'Help.xml' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\dist\\' }) {
        $dir = Split-Path $helpXml.FullName -Parent
        Write-Host "`nvalidating $($helpXml.FullName.Replace("$RepoRoot\", ''))" -ForegroundColor Cyan
        $problems = [System.Collections.Generic.List[string]]::new()

        try { [xml]$hx = Get-Content $helpXml.FullName -Raw } catch { $hx = $null; $problems.Add("does not parse: $($_.Exception.Message)") }

        # XPath rather than property access: $hx.Pack.Topic.VLDocument throws under Set-StrictMode
        # as soon as any Topic has no children, which is exactly the shape a half-edited Help.xml
        # has. Found by negative-testing this check with one entry deleted.
        [array]$entries = if ($hx) { @($hx.SelectNodes('//VLDocument')) } else { @() }
        if ($hx) {
            [array]$listed = $entries | ForEach-Object { $_.link }
            foreach ($link in $listed) {
                if (-not (Test-Path (Join-Path $dir $link))) { $problems.Add("lists `"$link`", which does not exist") }
            }
            foreach ($vl in Get-ChildItem $dir -Filter '*.vl' -File) {
                if ($listed -notcontains $vl.Name) { $problems.Add("`"$($vl.Name)`" is not listed - it would ship unordered and untagged") }
            }
            # Measured across every shipped .vl: 251 multi-term tag lists use commas and none use
            # spaces, which contradicts the written guidelines. Follow the shipped code.
            foreach ($e in $entries) {
                if ($e.tags -match ' ') { $problems.Add("tag list contains a space: `"$($e.tags)`"") }
                if (-not $e.tags) { $problems.Add("`"$($e.link)`" has no tags - it will not be findable by search") }
            }
            # An empty Topic renders as a heading with nothing under it.
            foreach ($t in @($hx.SelectNodes('//Topic'))) {
                if (@($t.SelectNodes('VLDocument')).Count -eq 0) { $problems.Add("Topic `"$($t.title)`" is empty") }
            }
        }

        if ($problems.Count -gt 0) {
            $problems | ForEach-Object { Write-Host "  FAIL  $_" -ForegroundColor Red }
            $totalProblems += $problems.Count
        } else {
            Write-Host "  ok    $($entries.Count) patch(es) listed, all present, none missing" -ForegroundColor DarkGray
            Write-Host "  ok    every entry tagged, no spaces in any tag list" -ForegroundColor DarkGray
            Write-Host "  ok    no empty Topic" -ForegroundColor DarkGray
        }
    }
}

Write-Host ''
if ($totalProblems -gt 0) {
    Write-Host "FAIL - $totalProblems problem(s) across $($targets.Count) document(s)." -ForegroundColor Red
    exit 1
}
Write-Host "PASS - $($targets.Count) document(s) structurally valid." -ForegroundColor Green
Write-Host @"
Note: this proves the documents are well formed, and nothing more. Three separate claims follow it:
  a node RESOLVED       -> vvvvc, then READ the generated *.vl.1.cs (exit code 0 proves nothing;
                           an unresolved node has its links dropped silently)
  its CATEGORY is right -> only the NodeBrowser. LastCategoryFullName in a .vl is a hint, and a
                           patch compiles fine with it set to nonsense
  it COMPUTES the right value -> only running it
"@ -ForegroundColor Yellow
