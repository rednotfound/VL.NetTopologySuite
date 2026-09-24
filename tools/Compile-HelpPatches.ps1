<#
.SYNOPSIS
    Compiles every help patch headlessly with vvvvc and checks that this package's nodes RESOLVED.

.DESCRIPTION
    This is the only automated check that says anything about whether a node actually works.
    Test-VLPatch.ps1 proves the XML is well formed; Test-VLPackage.ps1 proves the package could
    contribute nodes. Neither notices that a node failed to resolve.

    AND THE EXIT CODE OF vvvvc DOES NOT NOTICE EITHER. An unresolved node is built with no working
    pins, every link to it is dropped, and vvvvc exits 0 with nothing red. So this script does not
    trust the exit code: it READS the generated C# and asserts that every node the patch takes from
    VL.NetTopologySuite appears in it. That failure is total here, not partial - every node in this
    package names an NTS type - so a patch in which nothing resolved looks exactly like a patch in
    which everything did, until the C# is read.

    Carried from vl-geojson\tools\Compile-HelpPatches.ps1, where two things were learned that both
    have to line up:

      1. `--package-repositories` - how vvvv finds a *package folder*, i.e. dist\VL.NetTopologySuite\.
         Point it at dist\, not dist\feed\, or vvvvc says "Missing package".

      2. a NuGet source carrying the *nupkg* - because vvvvc then generates a .csproj with a
         PackageReference and runs a normal restore. Point that at dist\feed\, or restore says
         "NU1101: package VL.NetTopologySuite not found". CLAUDE.md used to call that NU1101
         "expected, because it is the export stage after codegen". The C# was indeed already
         written, but the export half never ran, so half of what a compile proves was never proved.
         A NuGet.config dropped at the output root fixes it; restore finds it by walking up.

    Requires .\pack.ps1 to have run: dist\feed\*.nupkg is what (2) reads, and pack.ps1 evicts the
    global NuGet cache entry, which is what makes the NU1101 check able to fail at all. Running this
    twice without repacking proves less the second time, because restore is then satisfied from
    %USERPROFILE%\.nuget\packages and never consults a source.

    Pass ABSOLUTE paths for both the document and the output directory: with a relative document
    path vvvvc dies inside PathUtils.GetRelativePath with "UriFormatException: Invalid URI".

    What it still cannot tell you: which CATEGORY a node lands in. LastCategoryFullName in a .vl
    is a hint - negative-tested here by setting it to NTS.Wrong, after which every node still
    resolved. Only the NodeBrowser proves that, and only running the patch proves it computes the
    right value.

.PARAMETER Patch
    Compile only patches whose name matches this wildcard. Default: all of them, and that is the
    gate - "compile every help patch, not the one you edited". Deleting a node once produced
    "Not found" in a file nobody had touched, in a sibling repository.

.PARAMETER KeepOutput
    Leave the generated projects in place so the *.vl.1.cs can be read by hand. The path is
    printed at the end.

.EXAMPLE
    .\pack.ps1 ; .\tools\Compile-HelpPatches.ps1
.EXAMPLE
    .\tools\Compile-HelpPatches.ps1 -Patch "*Buffer*" -KeepOutput
#>
param(
    [string]$OutputDirectory = '',
    [string]$Patch = '*',
    [switch]$KeepOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Dist     = Join-Path $RepoRoot 'dist'
$Deps     = Join-Path $RepoRoot 'deps'
$Feed     = Join-Path $RepoRoot 'dist\feed'

if (-not (Test-Path $Dist)) { throw "dist\ not found. Run .\build.ps1 first." }
if (-not (Get-ChildItem $Feed -Filter '*.nupkg' -ErrorAction SilentlyContinue)) {
    throw "no .nupkg in dist\feed - run .\pack.ps1 first, or restore will fail with NU1101."
}

$vvvvc = & (Join-Path $PSScriptRoot 'Find-Vvvv.ps1') -Compiler
if (-not (Test-Path $vvvvc)) { throw "vvvvc.exe not found at '$vvvvc'" }

# Detect and refuse - never kill. A running vvvv holds the staged assemblies open, and killing it
# corrupts its session state. Same guard as build.ps1.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)
if ($running) { throw "vvvv is running (PID $($running.Id -join ', ')). Close it by hand, then run this again." }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "vlnts-compile-$PID"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null

# (2): restore walks up from the generated .csproj and finds this. nuget.org is listed as well
# because the generated project also references NetTopologySuite itself, which our feed does not
# carry; on a warm cache it is never consulted, on a cold one it is needed.
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nts-dist" value="$Feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $OutputDirectory 'NuGet.config') -Encoding utf8

# (1): package *folders*, not the feed.
$repositories = (@($Dist, $Deps) | Where-Object { Test-Path $_ }) -join ';'

$patches = @(Get-ChildItem (Join-Path $RepoRoot 'help') -Filter '*.vl' -Recurse -File |
             Where-Object { $_.BaseName -like $Patch } | Sort-Object Name)
if ($patches.Count -eq 0) { throw "No help patch matches -Patch '$Patch'" }

# Every node this package contributes, by the name the patch uses, and the C# a resolved call
# must contain. A static method compiles to `Class.Method(`; a process node to `new ClassNode(`.
# "Split" exists twice (NTS.Geometry splits a Coordinate, NTS.Feature splits a Feature), so its
# entry accepts either class - the category is a hint in the .vl and cannot be trusted to tell
# them apart.
#
# A node the patch names that is NOT in this table is itself a failure, so that adding a node
# to the package and forgetting it here shows up the first time a patch uses it.
$OurNodes = @{
    # NTS.Geometry - creation
    'Coordinate'         = 'GeometryNodes\.Coordinate\('
    'CoordinateZ'        = 'GeometryNodes\.CoordinateZ\('
    'Split'              = '(GeometryNodes|FeatureNodes)\.Split\('
    'GeometryFactory'    = 'GeometryNodes\.GeometryFactory\('
    'Point'              = 'GeometryNodes\.Point\('
    'LineString'         = 'GeometryNodes\.LineString\('
    'LinearRing'         = 'GeometryNodes\.LinearRing\('
    'Polygon'            = 'GeometryNodes\.Polygon\('
    'MultiPoint'         = 'GeometryNodes\.MultiPoint\('
    'MultiLineString'    = 'GeometryNodes\.MultiLineString\('
    'MultiPolygon'       = 'GeometryNodes\.MultiPolygon\('
    'GeometryCollection' = 'GeometryNodes\.GeometryCollection\('
    # NTS.Geometry - inspection
    'GeometryType'       = 'GeometryNodes\.GeometryType\('
    'IsEmpty'            = 'GeometryNodes\.IsEmpty\('
    'SRID'               = 'GeometryNodes\.SRID\('
    'IsValid'            = 'GeometryNodes\.IsValid\('
    'Area'               = 'GeometryNodes\.Area\('
    'Length'             = 'GeometryNodes\.Length\('
    'Centroid'           = 'GeometryNodes\.Centroid\('
    'Bounds'             = 'GeometryNodes\.Bounds\('
    'Coordinates'        = 'GeometryNodes\.Coordinates\('
    'Geometries'         = 'GeometryNodes\.Geometries\('
    # NTS.Feature
    'Feature'            = 'FeatureNodes\.Feature\('
    # NTS.IO
    'Read WKT'           = 'IONodes\.ReadWKT\('
    'Write WKT'          = 'IONodes\.WriteWKT\('
    # NTS.Operation
    'Buffer'             = 'OperationNodes\.Buffer\('
    'Intersection'       = 'OperationNodes\.Intersection\('
    'Union'              = 'OperationNodes\.Union\('
    'Difference'         = 'OperationNodes\.Difference\('
    'Distance'           = 'OperationNodes\.Distance\('
    'Nearest Points'     = 'OperationNodes\.NearestPoints\('
    'Intersects'         = 'OperationNodes\.Intersects\('
    'Contains'           = 'OperationNodes\.Contains\('
    'Within'             = 'OperationNodes\.Within\('
    # NTS.Index
    'SpatialIndex'       = 'new\s+[\w\.]*SpatialIndexNode\('
    'Query'              = 'IndexNodes\.Query\('
    # NTS.Network
    'Network'            = 'new\s+[\w\.]*NetworkNode\('
    'ShortestPath'       = 'NetworkNodes\.ShortestPath\('
}

# Process nodes must be constructed in __Create__ and only updated in Update - built per frame
# they would rebuild an index over 100,000 geometries sixty times a second, which is the whole
# reason they are process nodes. Node name -> C# class.
$OurProcessNodes = @{
    'SpatialIndex' = 'SpatialIndexNode'
    'Network'      = 'NetworkNode'
}

# Every node the patch takes from THIS package, by name. Anchored on LastDependency so that a
# "Split" or "Contains" from another library is not counted against us.
function Get-ClaimedNodes([string]$patchXml) {
    $pattern = 'LastDependency="VL\.NetTopologySuite\.vl">\s*(?:<[^>]+>\s*)*?<Choice Kind="(?:OperationCallFlag|ProcessAppFlag|ProcessNodeFlag|OperationFlag)" Name="([^"]+)"'
    [regex]::Matches($patchXml, $pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
}

$failures = New-Object System.Collections.Generic.List[string]

foreach ($p in $patches) {
    Write-Host "`ncompiling $($p.Name)" -ForegroundColor Cyan

    $outDir = Join-Path $OutputDirectory ($p.BaseName -replace '[^\w\-]', '_')
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item (Join-Path $OutputDirectory 'NuGet.config') $outDir

    $log = & $vvvvc $p.FullName '--package-repositories' $repositories '--output-directory' $outDir 2>&1 | Out-String

    if ($log -match 'NU1101') {
        $failures.Add("$($p.Name): restore failed with NU1101 - dist\feed is not reaching restore")
        Write-Host "  FAIL  NU1101 - dist\feed is not reaching restore" -ForegroundColor Red
    }

    $generated = @(Get-ChildItem $outDir -Recurse -Filter '*.vl.1.cs' -ErrorAction SilentlyContinue)
    if ($generated.Count -eq 0) {
        $failures.Add("$($p.Name): no C# was generated")
        Write-Host "  FAIL  no C# generated" -ForegroundColor Red
        Write-Host (($log -split "`n" | Select-Object -Last 12) -join "`n") -ForegroundColor DarkGray
        continue
    }

    $csharp   = ($generated | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $patchXml = Get-Content $p.FullName -Raw
    $claimed  = @(Get-ClaimedNodes $patchXml)

    if ($claimed.Count -eq 0) {
        Write-Host "  ok    compiled (uses no VL.NetTopologySuite node)" -ForegroundColor DarkGray
        continue
    }

    foreach ($nodeName in $claimed) {
        if (-not $OurNodes.ContainsKey($nodeName)) {
            $failures.Add("$($p.Name): '$nodeName' is taken from VL.NetTopologySuite but is not in this script's node table - add it")
            Write-Host "  FAIL  '$nodeName' is not in the node table" -ForegroundColor Red
            continue
        }
        if ($csharp -notmatch $OurNodes[$nodeName]) {
            $failures.Add("$($p.Name): '$nodeName' is in the patch but NOT in the generated C# - it did not resolve")
            Write-Host "  FAIL  '$nodeName' did not resolve" -ForegroundColor Red
        }
        else {
            Write-Host "  ok    '$nodeName' resolved" -ForegroundColor DarkGray
        }

        if ($OurProcessNodes.ContainsKey($nodeName)) {
            $class = $OurProcessNodes[$nodeName]
            # The FIRST "__Create__" in the file is a CALL, not the definition, and the parameter
            # list has its own parentheses - hence the lazy `.*?\)\{`. Carried from vl-geojson.
            $createBody = [regex]::Match(
                $csharp, 'public\s+[\w\.]+\s+__Create__\(.*?\)\{(?<body>.*?)\n        \}', 'Singleline')
            if ($createBody.Success -and $createBody.Groups['body'].Value -match "new\s+[\w\.]*$class\(") {
                Write-Host "  ok    '$nodeName' is constructed in Create, not Update" -ForegroundColor DarkGray
            }
            else {
                $failures.Add("$($p.Name): $class is not constructed in __Create__ - it would be rebuilt every frame")
                Write-Host "  FAIL  '$nodeName' is not constructed in Create" -ForegroundColor Red
            }
        }
    }
}

if ($KeepOutput) {
    Write-Host "`ngenerated projects kept at $OutputDirectory" -ForegroundColor DarkGray
}
elseif (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "  $f" -ForegroundColor Red }
    Write-Host "`nFAIL - $($failures.Count) problem(s)." -ForegroundColor Red
    exit 1
}

Write-Host "PASS - $($patches.Count) patch(es) compiled, every VL.NetTopologySuite node resolved, no NU1101." -ForegroundColor Green
Write-Host @"
Note: this proves the nodes RESOLVE. It does not prove which CATEGORY they appear under - only
the NodeBrowser does - nor that the patch computes the right value, which only running it does.
"@ -ForegroundColor Yellow

# Explicitly, because vvvvc's own exit code may still be sitting in $LASTEXITCODE.
exit 0
