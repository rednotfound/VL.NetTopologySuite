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

## Help flags — what makes F1 work

Pressing **F1 on a selected node opens the help patch in which that node carries a High help flag**,
and marks the node there with a bubble ("Click this bubble for Node Info"). Nothing else links a node
to a patch: not the filename, not the node merely being used in the patch. Low flags list the patch
under the node's Node Info instead. In the editor the flag is set with **Ctrl+H** on the node (once:
High, twice: Low, three times: cleared). In the `.vl` it is one element right after the node's
`</p:NodeReference>`:

```xml
<p:HelpFocus p:Assembly="VL.Lang" p:Type="VL.Model.HelpPriority">High</p:HelpFocus>
```

Measured on 2026-09-24: **511 of the 689 help patches shipped with vvvv 7.4 carry flags** — 1059
High, 77 Low; Explanation patches carry them too (125 High). Our first fifteen carried none, which
is why F1 found nothing. Now every one of the 39 nodes has exactly one High flag (a HowTo where one
is dedicated to it, the Explanation otherwise), `tools\Test-VLPatch.ps1` audits that, and
`tools\HelpPatchGen.ps1` writes them from one `Set-HelpFlags` line per patch. Verified end to end:
F1 on a Buffer node in a scratch document opened `HowTo Buffer a geometry` with the bubble on Buffer.

Two more things F1 needs: the patches must be **inside the package's `help\` folder as vvvv sees the
package** — `dist\VL.NetTopologySuite\help\` when launched with `--package-repositories .\dist`, so
`build.ps1` must have been run after the patch was written — and vvvv indexes them at start.

Sources: [Providing Help](https://thegraybook.vvvv.org/reference/extending/providing-help.html),
[Finding Help](https://thegraybook.vvvv.org/reference/hde/findinghelp.html).

## Showing the geometry — a drawn IOBox

Since 2026-11-07 the editor draws a Geometry wherever one is shown (`VL.NetTopologySuite.HDE.vl`,
see ARCHITECTURE "Drawing without a renderer"): an IOBox of a Geometry is a picture - polygons
filled with their holes cut out, lines stroked, points as pins or markers - and a Feature IOBox is
the picture with the attribute table under it. **Every help patch therefore shows the geometry it
talks about**, in a column of drawn IOBoxes right of the notes: `x = 940`, 180 × 120, one per
geometry that matters, labelled with what it is (`A`, `Union`, `Buffered`, `Path`), rows aligned
with the node that makes it. A patch about two geometries also shows them *together*: `Cons` →
`GeometryCollection` → an IOBox labelled `A and B` (Combine, Relate, Measure distance), and
Measure distance draws the gap itself as a `LineString` through the two nearest points. The
Explanation front door has a gallery of three (point, line, polygon with hole) at `x = 1300`.

**A patch of several independent nodes is a grid of cells, not a column** (the maintainer,
2026-11-08). `HowTo Make a shape` first fed one `Coordinate` into all seven shape nodes and drew
them in the `x = 940` column: every link ran to a helper that does not matter, and each drawing sat
far from its node and its note. Now each shape is a cell, 3 across (`x` 60 / 540 / 1020, rows 340
apart): its own `Coordinate` and value IOBoxes above, the node, the drawing right under the node
(no label: it would run into the note), the note beside the drawing. The shared idea - a Vector2
from VL becomes a Coordinate - is a cell of its own.

The box is an ordinary readout Pad with a `Comment` and no `Value`, linked from the node's output;
vvvv types it from the link. **A dictionary readout needs room**: at 58 or 100 pixels high the
editor collapses a `Dictionary<String, Object>` to its type name, at 249 × 200 it lists the rows -
measured 2026-11-07 on `HowTo Attach attributes`; it lists `TooltipNumberOfItemsOfCollection` rows
(the editor's setting, 3) and an ellipsis, which is the editor's own grid and stays. A Feature
IOBox lists every attribute under its drawing, so give it 220 × 290 for a point with four
attributes. The same size rule holds for a `Spread<Object>` readout (measured on a Values readout, 2026-11-07):
at 80 high it shows `Spread<Object> [4]`, at 140 it lists the items with their types.

**A table in a patch is two Spread IOBoxes** (measured 2026-11-07 on `HowTo Attach attributes`, which went back to an `Add` chain the same night; the recipe stands): a Pad typed
`Spread` of `String` (`LastCategoryFullName="Collections" LastDependency="VL.Collections.vl"`, type
argument `String` from `CoreLibBasics.vl`) with the rows as `<p:Value><Item>name</Item>…</p:Value>`;
the editor shows the count in the header and one editable row per item. Keys at 120 wide, Values
at 160, 80 high for four rows, and 70 px of clear space after the Keys box for its label.
Nothing else is needed - no Renderer, no Skia nodes - which is why
this column replaced none of the existing notes and moved none of the maintainer's layout: it was
appended beside it. The Skia recipe below is still the way to draw *in the patch's own window*,
which the shortest-path patch does to animate; for *seeing a value*, the IOBox is enough.

## Drawing the result with VL.Skia

A help patch may use VL.Skia even though this package does not depend on it: VL.Skia ships inside
vvvv, and the community's help draws with it everywhere. The document just declares
`<NugetDependency Location="VL.Skia" ... />` (`Save-Doc -Dependencies 'VL.Skia'`).

NTS geometry reaches Skia through a **ForEach region**: `Coordinates` → ForEach { `Split` →
`ToFloat32` ×2 → `Vector (Join)` → `/ (Scale)` } → `AddPoly` (category `Graphics.Skia.Paths`) strings
the points into a Path and `DrawPath` draws it. **Set `AddPoly`'s `Close` pin to False explicitly**
(`DefaultValue="False"` on the pin): it defaults to True, from SkiaSharp's `AddPoly(points, close = true)`,
and a closed street draws a line back to its start - measured 2026-09-25, the first render was a zigzag.
**VL.Skia has no Polyline layer**: `Line` takes two points, `Polygon` with `Closed` off would draw the same line but no shipped
patch does that (0 of 10 `Polygon` uses), and the user asked why a polyline was called a polygon. A
spread of geometries needs a second, outer ForEach with one `AddPoly` + `DrawPath` per slice and
`Group (Spectral)` to collect the layers. A Skia **Paint is made outside the shape** — `Stroke`
(Color, Stroke Width) or `Fill` (Color) — and handed to the shape's `Paint` pin; wiring a layer into
`Stroke`'s Input fails with `Layer is no SkiaPaint!`. `Group` collects the layers, `Renderer` shows
them; its `Bounds` pin default places the window. The renderer's normalized space is −1..1 in
height, y down, wider with the window's aspect, so divide data coordinates to fit (the network
HowTo uses a town centred on the origin, ÷4, in a 900 × 450 window).

**A static node in front of `SpatialIndex` or `Network` needs a `Cache` region.** Measured on
2026-09-24: `Read WKT` parses every frame and returns new geometry objects every frame, and those
two nodes rebuild whenever the geometry *references* change — `Read WKT → Geometries → Network`
showed Networks Built at 1819 after thirty seconds. `Read WKT ×6 → Cons → Network` stayed at 1, but
only by accident: VL's collection builder behind `Cons` keeps the old items when the new ones are
`Equals`, and NetTopologySuite compares geometry by value. The honest fix in a patch is the
community's `Cache` region around the parse: it runs its body only when what enters through its
**Top control point** changes and hands the same objects out otherwise (`CacheRegion` in the
generator). The Top control point is not optional here — a link crossing the border without one is
not tracked as an input, the generated `CacheManager<ValueTuple, …>` has nothing to compare, and the
body never sees the text (Nodes 0, path EMPTY). With it: Nodes 19, Edges 22, Networks Built 1.

`tools\HelpPatchGen.ps1` writes all of it: `Region` (nestable; link the spread into `.Top`, `.Top`
into the first inner pin, the last output into `.Bottom`, `.Bottom` onward — every link lives in
the outer patch), `-Region` on `Node`, `-Defaults` for pin values without an IOBox, `-CategoryRef`
for `Vector (Join)`'s `Vector2Type` reference. The region XML shape was copied from a shipped
patch; `HowTo Find the shortest path` is the worked example, seen rendering on 2026-09-24.
## Sizing (measured, so it need not be re-derived)

| | 9pt body | 20pt heading |
|---|---|---|
| line height | ~18 px | ~41 px box for one line |
| glyph width | ~6.3 px → about `width / 6.3` characters per line | — |
| typical box | 210–350 wide, 20–90 tall | 170–370 wide, 30–41 tall |

A Pad's `Comment` label renders to the right of the box at ~6.5 px per character;
`tools\Test-VLPatch.ps1` counts it in the overlap arithmetic.

## Layout — measured from the maintainer's hand edits (2026-11-14)

The maintainer said the generated patches computed the right thing but their layout was unreadable,
and every one had to be re-arranged by hand. So the re-arrangements were measured: 20 patches,
before (as generated) and after (as the maintainer left them), in commits `65d8118`, `6ce539c`,
`f3c0b36` and `7188671`. The script is `layout_diff.py`, kept only in the session scratchpad; the
numbers are here.

| | as generated | after the maintainer |
|---|---|---|
| links running **upward** | 1–7 per patch | **0** (the most left anywhere is 19 px, a box beside its node) |
| canvas width | 1120–1469 | **~900** for a chain; a grid of cells is wider (`HowTo Make a shape`, 1460) |
| drawn result from the node that makes it (median dx) | 880–1160 (the old `x = 940` column) | **200–400**, beside or under it |
| vertical gap between linked elements (median) | 21–31 px | **40–50 px** |
| canvas height | | grows: width is traded for height |
| link crossings | 9–28 | fewer (7–16); a fan-out to many predicates keeps some |

The rules a generated patch follows, then:

1. **Data flows down.** No link runs upward; a box beside its node on the same row is the only
   exception.
2. **One cell per featured node**, with its own inputs above it, its drawn result directly under
   or beside it, and its note to the right (the cells-not-hubs rule of 2026-11-08). A row of cells
   480 px apart, as `HowTo Make a shape`, or one column of cells.
3. **40–50 px between linked rows**; parameter IOBoxes on their own row above the node, each
   above the pin it feeds, 90 px apart so a label does not reach the next box.
4. **Show input and result together** when the node changes a shape: `Cons` → `GeometryCollection`
   into one drawn IOBox, or the original in its own box beside the result. A piece of a line
   counts: `HowTo Walk along a line` was handed over with the Substring cell drawing only the
   growing piece, and the maintainer added the whole route in a box beside it (2026-11-14).
5. **Pick values that read at two decimals.** An IOBox shows `0.125` as `0.12`.

`Test-VLPatch.ps1` warns on the first and on a drawn result more than 300 px from its node, and
neither fires on any patch the maintainer arranged. Then the patch is **opened in vvvv and captured
before it is handed over** (`PrintWindow` on the launcher's own pid): the first tier-2 patch was
fixed twice that way (a label touching the next box, a rotation whose result lined up with the
original into a different shape), before the maintainer saw it.

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
