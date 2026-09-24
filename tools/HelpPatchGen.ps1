<#
.SYNOPSIS
    Library for GENERATING a new help patch (.vl) from a compact PowerShell description. Dot-source it.

.DESCRIPTION
    The fifteen help patches in help\ were written this way. The XML shapes are copied from shipped
    patches, IDs are generated, pins are named by the caller and checked afterwards by reading the C#
    that tools\Compile-HelpPatches.ps1 produces: a wrong pin name does not fail the compile, it makes
    the wired input read default(...) in the generated code.

    THE STYLE IS THE COMMUNITY'S, MEASURED - see docs\HELP-PATCH-STYLE.md. Heading (20pt, one line,
    an instruction or the topic), an optional short Intro (9pt, under 250 characters), the wired
    nodes, and Notes beside them (9pt, "< ..." pointing at the thing, under 150 characters). Not
    essays: the median community note is 34 characters long.

    ONCE A PATCH IS CHECKED IN, THE .vl IS THE SOURCE OF TRUTH, NOT THE SCRIPT THAT MADE IT. Layout
    fixes after a GUI check are made in the .vl (Bounds edits anchored on a match asserted to occur
    once), so a generating script is a one-shot scaffold and is deliberately not kept beside the patch.

    Sizing at 9pt: ~18 px per line, ~6.3 px per character. A Pad's Comment label renders to the RIGHT
    of the box at ~6.5 px per character; Test-VLPatch.ps1 does that arithmetic.

.EXAMPLE
    . .\tools\HelpPatchGen.ps1
    $d = New-Doc
    Heading $d 60 40 'Use Read WKT!'
    Intro   $d 60 90 'Well-Known Text is the plain-text form of a geometry.'
    $wkt = Pad $d String '60,170,400,15' 'POINT (1 1)' 'WKT'
    $r = Node $d 'Read WKT' 'NTS.IO' '60,220,90,19' -In 'WKT','Factory' -Out 'Result','Success'
    Link $d $wkt $r.WKT
    Link $d $r.Result (OutPad $d '60,280,300,15' 'Geometry')
    Note $d 580 220 'Break the text on purpose: Success goes False, the geometry goes empty. Never throws.'
    Save-Doc $d '.\help\VL.NetTopologySuite\HowTo Something.vl'
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
    [pscustomobject]@{
        Elements = [System.Collections.Generic.List[string]]::new()
        Links    = [System.Collections.Generic.List[string]]::new()
        # bottom edge of the last Note per column (X), so a Note never lands on the one above it
        NoteBottom = @{}
        # node name -> 'High' | 'Low': the help flag every Node of that name in this document gets
        HelpFlags = @{}
    }
}

# HELP FLAGS ARE WHAT MAKES F1 WORK. Pressing F1 on a node opens the help patch in which that node
# carries a High flag (<p:HelpFocus ...>High</p:HelpFocus> right after </p:NodeReference>, which
# is what Ctrl+H writes in the editor); Low flags list the patch under the node's Node Info. 511 of
# the 689 help patches shipped with vvvv 7.4 carry flags. One High per node across the library.
function Set-HelpFlags($d, [string[]]$High = @(), [string[]]$Low = @()) {
    foreach ($n in $High) { $d.HelpFlags[$n] = 'High' }
    foreach ($n in $Low)  { $d.HelpFlags[$n] = 'Low' }
}

# An annotation box: stringtype Comment, no Comment attribute, so Test-VLPatch knows it is prose.
function Box($d, [string]$Bounds, [string]$Text, [int]$FontSize = 9) {
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

# The one-line 20pt heading at the top: an instruction ("Use Buffer!") or the topic.
function Heading($d, [int]$X, [int]$Y, [string]$Text) {
    $w = [int](15 * $Text.Length + 30)
    Box $d "$X,$Y,$w,41" $Text 20
}

# Measured in the GUI (2026-09-24): 9pt wraps at ~7.3 px per character (320 px -> 44 characters), 18 px per line.
function Get-TextLines([string]$Text, [int]$Width) {
    $perLine = [Math]::Floor($Width / 7.3)
    $lines = 0
    foreach ($para in ($Text -split "`r?`n")) { $lines += [Math]::Max(1, [Math]::Ceiling($para.Length / $perLine)) }
    $lines
}

# One short 9pt paragraph under the heading. Width 480 -> ~65 characters per line.
function Intro($d, [int]$X, [int]$Y, [string]$Text, [int]$Width = 480) {
    if ($Text.Length -gt 260) { throw "Intro is $($Text.Length) characters - keep it under 260: $Text" }
    Box $d "$X,$Y,$Width,$([int](18 * (Get-TextLines $Text $Width) + 10))" $Text 9
}

# A 9pt note beside a node or IOBox, starting with "< ". Width 320 -> ~44 characters per line.
# If it would land on the previous Note in the same column it is pushed down below it.
function Note($d, [int]$X, [int]$Y, [string]$Text, [int]$Width = 320) {
    if ($Text -notmatch '^<') { $Text = '< ' + $Text }
    if ($Text.Length -gt 170) { throw "Note is $($Text.Length) characters - keep it under 170: $Text" }
    if ($d.NoteBottom.ContainsKey($X) -and $Y -lt $d.NoteBottom[$X] + 8) { $Y = $d.NoteBottom[$X] + 8 }
    $h = [int](18 * (Get-TextLines $Text $Width) + 8)
    $d.NoteBottom[$X] = $Y + $h
    Box $d "$X,$Y,$Width,$h" $Text 9
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
# -Spread adds the CategoryReference a Collections.Spread node carries; -RecordType 'Dictionary' the
# one a Collections.Dictionary node carries. Returns an object whose properties are the pin ids, named
# after the pins with spaces removed ('Candidate Count' -> CandidateCount).
function Node($d, [string]$Name, [string]$Category, [string]$Bounds,
              [string[]]$In = @(), [string[]]$Out = @(),
              [string]$Kind = 'OperationCallFlag', [string]$Dependency = 'VL.NetTopologySuite.vl',
              [switch]$Spread, [string[]]$StateIn = @(), [string]$RecordType = '', [string[]]$StateOut = @(),
              [ValidateSet('', 'High', 'Low', 'None')][string]$HelpFocus = '') {
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
    # -HelpFocus None: this instance carries no flag even though the document flags its name
    # (the second Contains in HowTo Test how geometries relate, wired the other way round).
    $flag = if ($HelpFocus -eq 'None') { '' } elseif ($HelpFocus) { $HelpFocus } elseif ($d.HelpFlags.ContainsKey($Name)) { $d.HelpFlags[$Name] } else { '' }
    if ($flag) { $lines.Add("            <p:HelpFocus p:Assembly=`"VL.Lang`" p:Type=`"VL.Model.HelpPriority`">$flag</p:HelpFocus>") }
    # $pid is PowerShell's read-only process id - hence $pinId.
    foreach ($p in $StateIn)  { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateInputPin`" />") }
    foreach ($p in $In)       { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"InputPin`" />") }
    foreach ($p in $Out)      { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"OutputPin`" />") }
    foreach ($p in $StateOut) { $pinId = New-Id; $pins[($p -replace '\s', '')] = $pinId; $lines.Add("            <Pin Id=`"$pinId`" Name=`"$p`" Kind=`"StateOutputPin`" />") }
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
