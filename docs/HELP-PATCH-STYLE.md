# Help patch style — measured from the VL community, not invented here

**Read this before writing or rewriting a help patch.** The first fifteen patches in this repository
(2026-09-24) were written as essays: a 900-pixel box of prose at the top opening with "One idea:",
then two or three more paragraphs of 300–600 characters each. That is not how the community writes
help, and the user noticed before the author did. Everything below was measured on 2026-09-24 against
the packs shipped with vvvv gamma 7.4 and the community packs in the local NuGet cache; the numbers are
reproducible with the PowerShell in the appendix.

## What was measured

| corpus | files | notes per patch | note length p50 / p90 (chars) | notes starting with `<` | body font |
|---|---|---|---|---|---|
| VL.CoreLib + VL.Skia + VL.Stride (vvvv 7.4) | 317 HowTo | 3 | 34 / 149 | 303 of 1859 | 9pt (71 % of all boxes) |
| VL.Fuse 1.0.0-alpha04 | 124 | 7.8 | 25 / 193 | few | 9pt, 13pt |
| VL.Elementa 5.0.12 | 79 | 7.8 | 50 / 184 | few | 9pt |
| VL.PolyTools 1.4.0 | 75 | 5.6 | 76 / 293 | 167 of 407 | 9pt |
| VL.OpenCV 2.1.0 | 50 | 9.1 | 38 / 146 | few | 9pt, 13pt |
| VL.ImGui 2023.5.0 | 82 | 3 | 25 / 138 | 24 of 171 | 9pt, headings 14/18pt |
| VL.ExtendedTutorials 1.1.1 | 35 (31 `Explanation`) | 21 | 81 / 325 | 186 of 739 | 9pt |

Across the 1480 annotation boxes in the three core packs: median box **210 × 32 px**, median text
**34 characters**, and only **21 boxes exceed 400 characters**. Our first drafts had fifteen boxes over
400 characters in fifteen patches.

## The shape of a HowTo

1. **A heading box, top-left, one line, 14–20pt.** 235 of 389 annotated core HowTos have one, 219 of
   those at the very top. It is either an instruction — `Use a MonoFlop!`, `Use a Split (Count) node!`,
   `Use Trim PathEffect` — or the topic in two words — `Clipping`, `Vignetting`, `Texture overlay`.
   It is *not* a sentence about ideas. The filename already says what the patch is about.
2. **Optionally one short paragraph under it, 9pt, two to four lines** (100–250 characters): what
   this example shows, in the plainest words. `This example shows how to clip layers (however
   complex they are) by a rectangle.`
3. **The nodes, wired and computing.** Values visible in IOBoxes with short `Comment` labels of one
   or two words: `Period`, `Start`, `Stop`, `Retriggerable`.
4. **Notes beside the nodes, 9pt, 20–150 characters, many starting with `<`** pointing at the thing
   they describe: `< Bang to set the MonoFlop's output to 1 for the given period of time.`,
   `< Disconnect the PathEffect to see the full path.` Imperative where possible: *try*, *connect*,
   *disconnect*, *change*. A note explains one pin, one behaviour, one thing to try — never the
   philosophy of the library.
5. **Numbered steps when order matters**: `1.` `2.` `3.` as 20pt boxes with a 9pt note beside each
   (VL.Stride `HowTo Work with Children`).
6. **Warnings are short and loud**: `THE DISTANCE IS IN THE GEOMETRY'S OWN UNITS` is fine as one
   line; a paragraph about the poles is not.

Layout: heading around (30–140, 76–142); everything else below it; notes to the *right* of the node
or IOBox they belong to, so the eye reads node-then-note. Canvas width rarely exceeds 1000 px.

## The shape of an Explanation Overview

VL.IO.Redis: the package's nodes placed on the canvas, unwired, each with one `< sentence` beside it —
six boxes for six nodes. VL.Audio: one intro paragraph, then category labels with the nodes under each.
Either way the front door **shows the nodes**; it does not describe the package in four essays.

## Sizing (measured, so it need not be re-derived)

| | 9pt body | 20pt heading |
|---|---|---|
| line height | ~18 px | ~41 px box for one line |
| glyph width | ~6.3 px → about `width / 6.3` characters per line | — |
| typical box | 210–350 wide, 20–90 tall | 170–370 wide, 30–41 tall |

A Pad's `Comment` label renders to the right of the box at ~6.5 px per character;
`tools\Test-VLPatch.ps1` counts it in the overlap arithmetic.

## What this repository does with it

- Every HowTo: heading (instruction or topic) → optional one-paragraph intro → wired nodes → `<` notes
  beside nodes, 9pt, under 150 characters each, at most one longer warning line.
- No "One idea:" openers, no 900-pixel boxes, no paragraphs about design decisions — those live in
  `docs/ARCHITECTURE.md`, which a help patch may name in one line if it must.
- The Explanation shows the nodes by category with one line each.
- `tools\HelpPatchGen.ps1` emits 9pt by default and takes `-FontSize 20` for the heading.

## Appendix — how to re-measure

```powershell
# every annotation box (stringtype Comment) in a folder of .vl files: size, font, length, first chars
Get-ChildItem <folder> -Recurse -Filter *.vl | ForEach-Object {
  $raw = [IO.File]::ReadAllText($_.FullName)
  [regex]::Matches($raw, '<Pad [^>]*Bounds="(\d+),(\d+),(\d+),(\d+)"[^>]*Value="([^"]*)"[^>]*>(?<b>(?:(?!</Pad>).)*)</Pad>', 'Singleline') |
    Where-Object { $_.Groups['b'].Value -match 'StringType">Comment<' } |
    ForEach-Object {
      $fs = 9; if ($_.Groups['b'].Value -match 'fontsize p:Type="Int32">(\d+)<') { $fs = [int]$Matches[1] }
      [pscustomobject]@{ File = $_.Groups[0].Value.Length; W = [int]$_.Groups[3].Value; H = [int]$_.Groups[4].Value; Font = $fs
                         Chars = ([System.Net.WebUtility]::HtmlDecode($_.Groups[5].Value)).Length }
    }
}
```

Shipped packs: `C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\packs\*\help`. Community packs:
`%USERPROFILE%\.nuget\packages\vl.*\<version>\help`.
