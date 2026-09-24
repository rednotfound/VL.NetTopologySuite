<#
.SYNOPSIS
    Library for GENERATING a new help patch (.vl) from a compact PowerShell description. Dot-source it.

.DESCRIPTION
    Ten of the HowTo patches in help\ were first written this way (2026-09-24). The XML shapes are copied
    from shipped patches, IDs are generated, pins are named by the caller and checked afterwards by reading
    the C# that tools\Compile-HelpPatches.ps1 produces: a wrong pin name does not fail the compile, it makes
    the wired input read default(...) in the generated code.

    ONCE A PATCH IS CHECKED IN, THE .vl IS THE SOURCE OF TRUTH, NOT THE SCRIPT THAT MADE IT. Layout fixes
    after a GUI check are made in the .vl (Bounds edits anchored on a match asserted to occur once), so a
    generating script is a one-shot scaffold and is deliberately not kept beside the patch.

    Measured for sizing annotation boxes (font 11): 22.5 px per line, one blank line per paragraph gap,
    ~55 characters per line at 440 px wide, ~104 at 900 px. A Pad Comment renders to the RIGHT of the box,
    6.5 px per character; Test-VLPatch.ps1 does that arithmetic.

.EXAMPLE
    . .\tools\HelpPatchGen.ps1
    $d = New-Doc
    Box  $d 60,60,900,150 One idea: ...
    $wkt = Pad $d String 60,250,460,15 POINT (1 1) edit me
    $r = Node $d Read WKT NTS.IO 60,300,90,19 -In WKT,Factory -Out Result,Success
    Link $d $wkt $r.WKT
    Link $d $r.Result (OutPad $d 60,360,300,15 what came out)
    Save-Doc $d .\help\VL.NetTopologySuite\HowTo Something.vl
    # then: add it to Help.xml, Test-VLPatch, pack + Compile-HelpPatches (read the C#), open it in vvvv.
#>
Set-StrictMode -Version Latest

$script:FirstChars = [char[]]'ABCDEFGHIJKLMNOPQRSTUV'
$script:RestChars  = [char[]]'0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
$script:Rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
function Get-RandomIndex([int]$upperBound) {
    $limit = [int]([Math]::Floor(256 / $upperBound)) * $upperBound
    $buf = [byte[]]::new(1)
    do { $script:Rng.GetBytes($buf) } while ($buf[0] -ge $limit)
    return $buf[0] % $upperBound
}
function New-Id {
    $sb = [System.Text.StringBuilder]::new(22)
    [void]$sb.Append($script:FirstChars[(Get-RandomIndex $script:FirstChars.Length)])
    for ($i = 1; $i -lt 22; $i++) { [void]$sb.Append($script:RestChars[(Get-RandomIndex $script:RestChars.Length)]) }
    $sb.ToString()
}
function Esc([string]$t) {
    $t = ($t -replace "`r`n", "`n").Trim("`n")
    $t = $t.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
    $t.Replace("`n", '&#xD;&#xA;')
}

function New-Doc {
    [pscustomobject]@{ Elements = [System.Collections.Generic.List[string]]::new(); Links = [System.Collections.Generic.List[string]]::new() }
}

function Box($d, [string]$Bounds, [string]$Text, [int]$FontSize = 11) {
    $id = New-Id
    $d.Elements.Add(@(
        "          <Pad Id=`"$id`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" Value=`"$(Esc $Text)`">",
        '            <p:TypeAnnotation>',
        '              <Choice Kind="TypeFlag" Name="String" />',
        '            </p:TypeAnnotation>',
        '            <p:ValueBoxSettings>',
        "              <p:fontsize p:Type=`"Int32`">$FontSize</p:fontsize>",
        '              <p:stringtype p:Assembly="VL.Core" p:Type="VL.Core.StringType">Comment</p:stringtype>',
        '            </p:ValueBoxSettings>',
        '          </Pad>') -join "`r`n")
    [void]$id
}

# A value IOBox: Float64 | Integer32 | String | Boolean. Returns its Id, which is also its pin id.
function Pad($d, [string]$Type, [string]$Bounds, [string]$Value, [string]$Comment = '') {
    $id = New-Id
    $d.Elements.Add(@(
        "          <Pad Id=`"$id`" Comment=`"$(Esc $Comment)`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" Value=`"$(Esc $Value)`">",
        '            <p:TypeAnnotation LastCategoryFullName="Primitive" LastDependency="VL.CoreLib.vl">',
        "              <Choice Kind=`"TypeFlag`" Name=`"$Type`" />",
        '            </p:TypeAnnotation>',
        '          </Pad>') -join "`r`n")
    $id
}

# An output IOBox with no value and no type - VL types it from the link. Returns its Id.
function OutPad($d, [string]$Bounds, [string]$Comment = '') {
    $id = New-Id
    $d.Elements.Add("          <Pad Id=`"$id`" Comment=`"$(Esc $Comment)`" Bounds=`"$Bounds`" ShowValueBox=`"true`" isIOBox=`"true`" />")
    $id
}

# A node. -Kind OperationCallFlag (static method) or ProcessAppFlag (process node).
# -Spread adds the CategoryReference a Collections.Spread node carries. Returns an object whose
# properties are the pin ids, named after the pins (spaces removed: 'Candidate Count' -> CandidateCount).
function Node($d, [string]$Name, [string]$Category, [string]$Bounds,
              [string[]]$In = @(), [string[]]$Out = @(),
              [string]$Kind = 'OperationCallFlag', [string]$Dependency = 'VL.NetTopologySuite.vl',
              [switch]$Spread, [string[]]$StateIn = @(), [string]$RecordType = "", [string[]]$StateOut = @()) {
    $id = New-Id
    $pins = [ordered]@{}
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("          <Node Bounds=`"$Bounds`" Id=`"$id`">")
    $lines.Add("            <p:NodeReference LastCategoryFullName=`"$Category`" LastDependency=`"$Dependency`">")
    $lines.Add('              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />')
    $lines.Add("              <Choice Kind=`"$Kind`" Name=`"$Name`" />")
    if ($Spread) { $lines.Add('              <CategoryReference Kind="RecordType" Name="Spread" NeedsToBeDirectParent="true" />') }
    if ($RecordType) { $lines.Add("              <CategoryReference Kind=`"RecordType`" Name=`"$RecordType`" />") }
    $lines.Add('            </p:NodeReference>')
    # $pid is PowerShell's read-only process id - hence $pinId.
    foreach ($p in $StateIn) { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateInputPin`" />") }
    foreach ($p in $In)      { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"InputPin`" />") }
    foreach ($p in $Out)     { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"OutputPin`" />") }
    foreach ($p in $StateOut){ $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateOutputPin`" />") }
    $lines.Add('          </Node>')
    $d.Elements.Add($lines -join "`r`n")
    [pscustomobject]$pins
}

function Link($d, [string]$From, [string]$To) {
    if (-not $From -or -not $To) { throw "Link needs two pin ids" }
    $d.Links.Add("        <Link Id=`"$(New-Id)`" Ids=`"$From,$To`" />")
}

function Save-Doc($d, [string]$Path) {
    $docId = New-Id; $coreDep = New-Id; $patch = New-Id; $canvas = New-Id; $app = New-Id
    $appPatch = New-Id; $group = New-Id; $create = New-Id; $update = New-Id; $procDef = New-Id
    $frag1 = New-Id; $frag2 = New-Id; $ntsDep = New-Id
    $xml = @(
        '<?xml version="1.0" encoding="utf-8"?>',
        "<Document xmlns:p=`"property`" xmlns:r=`"reflection`" Id=`"$docId`" LanguageVersion=`"2025.7.4`" Version=`"0.128`">",
        "  <NugetDependency Id=`"$coreDep`" Location=`"VL.CoreLib`" Version=`"2025.7.4`" />",
        "  <Patch Id=`"$patch`">",
        "    <Canvas Id=`"$canvas`" DefaultCategory=`"Main`" BordersChecked=`"false`" CanvasType=`"FullCategory`" />",
        '    <!--',
        '',
        '    ************************ Application ************************',
        '',
        '-->',
        "    <Node Name=`"Application`" Bounds=`"100,100`" Id=`"$app`">",
        '      <p:NodeReference>',
        '        <Choice Kind="ContainerDefinition" Name="Process" />',
        '        <CategoryReference Kind="Category" Name="Primitive" />',
        '      </p:NodeReference>',
        "      <Patch Id=`"$appPatch`">",
        "        <Canvas Id=`"$group`" CanvasType=`"Group`">"
    ) + @($d.Elements) + @(
        '        </Canvas>',
        "        <Patch Id=`"$create`" Name=`"Create`" />",
        "        <Patch Id=`"$update`" Name=`"Update`" />",
        "        <ProcessDefinition Id=`"$procDef`">",
        "          <Fragment Id=`"$frag1`" Patch=`"$create`" Enabled=`"true`" />",
        "          <Fragment Id=`"$frag2`" Patch=`"$update`" Enabled=`"true`" />",
        '        </ProcessDefinition>'
    ) + @($d.Links) + @(
        '      </Patch>',
        '    </Node>',
        '  </Patch>',
        "  <NugetDependency Id=`"$ntsDep`" Location=`"VL.NetTopologySuite`" Version=`"0.0.0`" />",
        '</Document>'
    )
    $text = (($xml -join "`r`n") -replace "(?<!`r)`n", "`r`n") + "`r`n"
    [IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding $true))
    "wrote $Path ($($d.Elements.Count) elements, $($d.Links.Count) links)"
}
