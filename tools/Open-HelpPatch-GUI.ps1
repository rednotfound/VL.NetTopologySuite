<#
.SYNOPSIS
    A double-clickable picker for the help patches - select one, vvvv opens it.

.DESCRIPTION
    Wraps Open-HelpPatch.ps1, which stays the single source of truth for HOW a patch is
    launched: the two package repositories, the vvvv-already-running check, the empty-dist
    check. This file only supplies a window; every launch goes through the same gate and every
    refusal is printed in the log box with the same words.

    Start it by double-clicking Open-HelpPatch.cmd in the repository root, or:

        pwsh -File tools\Open-HelpPatch-GUI.ps1

    The Normalize button runs tools\Normalize-HelpPatches.ps1, the Check button runs
    tools\Test-VLPatch.ps1 - both for AFTER a hand edit was saved and vvvv closed.

    Carried from vl-overworld\tools\Open-HelpPatch-GUI.ps1.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$RepoRoot  = Split-Path $PSScriptRoot -Parent
$Launcher  = Join-Path $PSScriptRoot 'Open-HelpPatch.ps1'
$Normalize = Join-Path $PSScriptRoot 'Normalize-HelpPatches.ps1'
$Check     = Join-Path $PSScriptRoot 'Test-VLPatch.ps1'
$HelpDir   = Join-Path $RepoRoot 'help\VL.NetTopologySuite'

# The same enumeration Open-HelpPatch.ps1 uses, so the window never shows a patch the
# launcher would not find.
$patches = @(Get-ChildItem $HelpDir -File -Filter *.vl | Sort-Object Name)

$form                 = [System.Windows.Forms.Form]::new()
$form.Text            = 'VL.NetTopologySuite - open a help patch'
$form.ClientSize      = [System.Drawing.Size]::new(640, 620)
$form.StartPosition   = 'CenterScreen'
$form.Font            = [System.Drawing.Font]::new('Segoe UI', 10)
$form.MinimumSize     = [System.Drawing.Size]::new(500, 500)

$list                 = [System.Windows.Forms.ListBox]::new()
$list.Location        = [System.Drawing.Point]::new(12, 12)
$list.Size            = [System.Drawing.Size]::new(616, 320)
$list.Anchor          = 'Top,Left,Right,Bottom'
$list.Font            = [System.Drawing.Font]::new('Segoe UI', 11)
$list.IntegralHeight  = $false
foreach ($p in $patches) { [void]$list.Items.Add($p.BaseName) }
if ($list.Items.Count -gt 0) { $list.SelectedIndex = 0 }

$hint                 = [System.Windows.Forms.Label]::new()
$hint.Text            = 'Opening a document in vvvv is RUNNING it. Read, adjust, save, close vvvv - then Normalize and Check.'
$hint.Location        = [System.Drawing.Point]::new(12, 340)
$hint.Size            = [System.Drawing.Size]::new(616, 20)
$hint.Anchor          = 'Left,Right,Bottom'

$openBtn              = [System.Windows.Forms.Button]::new()
$openBtn.Text         = 'Open in vvvv'
$openBtn.Location     = [System.Drawing.Point]::new(12, 366)
$openBtn.Size         = [System.Drawing.Size]::new(160, 34)
$openBtn.Anchor       = 'Left,Bottom'

$normBtn              = [System.Windows.Forms.Button]::new()
$normBtn.Text         = 'Normalize (vvvv closed)'
$normBtn.Location     = [System.Drawing.Point]::new(184, 366)
$normBtn.Size         = [System.Drawing.Size]::new(200, 34)
$normBtn.Anchor       = 'Left,Bottom'

$checkBtn             = [System.Windows.Forms.Button]::new()
$checkBtn.Text        = 'Check layout (Test-VLPatch)'
$checkBtn.Location    = [System.Drawing.Point]::new(396, 366)
$checkBtn.Size        = [System.Drawing.Size]::new(232, 34)
$checkBtn.Anchor      = 'Left,Bottom'

$log                  = [System.Windows.Forms.TextBox]::new()
$log.Multiline        = $true
$log.ReadOnly         = $true
$log.ScrollBars       = 'Vertical'
$log.Font             = [System.Drawing.Font]::new('Consolas', 9)
$log.Location         = [System.Drawing.Point]::new(12, 412)
$log.Size             = [System.Drawing.Size]::new(616, 196)
$log.Anchor           = 'Left,Right,Bottom'
$log.Text             = "pick a patch and press Open - or double-click it.`r`n"

# Run a tool script in a CHILD pwsh: the tools call `exit` on refusal, which would close this
# window if they ran in-process. The child's console output lands in the log box either way.
function Invoke-Tool([string]$scriptPath, [string[]]$toolArgs, [string]$doing) {
    $openBtn.Enabled = $false; $normBtn.Enabled = $false; $checkBtn.Enabled = $false
    $form.UseWaitCursor = $true
    $log.AppendText("`r`n== $doing`r`n")
    [System.Windows.Forms.Application]::DoEvents()
    try {
        $out = & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath @toolArgs 2>&1 | Out-String
        $log.AppendText(($out -replace "`e\[[\d;]*m", ''))
        if ($LASTEXITCODE -ne 0) { $log.AppendText("`r`nREFUSED / FAILED (exit $LASTEXITCODE) - the reason is above.`r`n") }
        $log.SelectionStart = $log.TextLength; $log.ScrollToCaret()
    }
    finally {
        $form.UseWaitCursor = $false
        $openBtn.Enabled = $true; $normBtn.Enabled = $true; $checkBtn.Enabled = $true
    }
}

$openPatch = {
    if ($list.SelectedIndex -lt 0) { return }
    $file = $patches[$list.SelectedIndex].FullName
    Invoke-Tool $Launcher @('-Path', $file) "opening $($list.SelectedItem)"
}

$openBtn.Add_Click($openPatch)
$list.Add_DoubleClick($openPatch)
$normBtn.Add_Click({ Invoke-Tool $Normalize @() 'normalizing help patches (vvvv must be closed)' })
$checkBtn.Add_Click({ Invoke-Tool $Check @() 'checking every .vl and Help.xml' })

$form.Controls.AddRange(@($list, $hint, $openBtn, $normBtn, $checkBtn, $log))
[void]$form.ShowDialog()
