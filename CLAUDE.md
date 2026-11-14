# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project overview

**VL.NetTopologySuite** wraps [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite)
as nodes for [vvvv gamma](https://vvvv.org). One package, one library — geometry creation,
inspection, spatial operations and WKT. Nothing about maps, rendering or reprojection.

**Current state (2026-11-08): `0.0.6-alpha` is on nuget.org** — `0.0.1-alpha` on 2026-09-26,
`0.0.2-alpha` (the category rename to `NetTopologySuite.*`) on 2026-10-17, `0.0.3-alpha`
(`GetAttribute`, the version VL.NetTopologySuite.GeoJSON depends on) and `0.0.4-alpha` (the tooltip
rewrite, no node changed) on 2026-10-18, `0.0.5-alpha` (tier 1, `Outlines`, the drawn previews) and
`0.0.6-alpha` (shapes, the sticky drawn IOBox, `FromWKT` / `ToWKT`) both on 2026-11-08, all by the
maintainer's browser upload, all installed back
from nuget.org alone with `Test-Install -FromNuGetOrg`. **The working version is now `0.0.7-alpha`**,
always one ahead of the published one. **Every version is a prerelease** (`-alpha` suffix, installed with
`-pre`), managed in step with VL.Mapsui and the family — the maintainer's rule, 2026-09-25. The
process and its rules are in [docs/RELEASE.md](docs/RELEASE.md): the maintainer performs the
irreversible step, and nuget.org's 2026 policy (API keys 30 days at most, Trusted Publishing for CI,
browser upload for manual) is written there. Never push to nuget.org without the user's explicit
go; a publish cannot be undone.** 64 public nodes in six categories (all published as of `0.0.6-alpha`, the VL-shaped rethink: seven shape nodes, `Coordinate (FromVector2)` (its `ToVector2` was dropped the same day: the one node that lost precision silently), and `Read WKT` / `Write WKT` renamed `FromWKT` / `ToWKT` with no alias, all on 2026-11-08) — `NetTopologySuite.Network` (`Network` +
`ShortestPath`, an algorithm of ours, promoted from `NetTopologySuite.Experimental.Network` by
`docs/NETWORK-SCOPE-PROPOSAL.md` after two consumers were rung-4 verified) is the sixth — 329
tests, 20 help patches (2026-11-14).** **66 since 2026-11-11: `GeometryEditor`, a geometry drawn by hand in an
editor-extension window** ([below](#the-geometry-editor-a-window-of-our-own-in-the-editor)), **and
`SetSRID`**, the label verb, paired with VL.PROJ's `Reproject` (`D:\2026_Projects\vl-proj`, the
family's sixth package): it says what CRS the numbers are in and moves nothing. Both are
unpublished, in the working `0.0.7-alpha`. All fifteen of the first wave were (re)written on 2026-09-24 in the community's measured style
([docs/HELP-PATCH-STYLE.md](docs/HELP-PATCH-STYLE.md)), every one seen in the GUI, every node opens one
on **F1** (help flags, audited by `Test-VLPatch`), and `HowTo Find the shortest path` draws its town with
VL.Skia through ForEach and Cache regions. The
2026-08-14 GUI verification covered `NetTopologySuite.Geometry` / `NetTopologySuite.IO` / `NetTopologySuite.Operation`; `NetTopologySuite.Feature`
(2026-08-22) was seen in the GUI through VL.Overworld's Tutorial 08 on 2026-08-23; **`NetTopologySuite.Index`
(2026-08-23) was seen the same evening through Tutorial 11**, with `Indexes Built` holding at 1 over
100,000 points. Every category has now been on a screen. `0.0.1-alpha` is on nuget.org (2026-09-26). See
[Verification](#verification-be-precise-about-which-one-you-have).

**Coverage, measured 2026-10-18, tier 1 landed 2026-11-07:** the 40 nodes wrapped 20 of the 64 members
of NTS's `Geometry` class (31%) and touched 10 of 24 user-facing capability areas; the 15 tier-1 nodes
take `Geometry` to 35 of 64 (55%), the predicates to 8 named of 9 plus `Relate`, the overlays to 4 of
4, and open the hull area. The maintainer's rule:
a package that takes the library's name owes its users what a geometry library is expected to do.
[docs/ROADMAP.md](docs/ROADMAP.md#what-the-name-obliges--coverage-measured-2026-10-18) orders the
gap in three tiers by user expectation (tier 1: the rest of `Geometry`'s own surface, 15 one-line
nodes; tier 2: union of many, simplify, WKB, affine transforms, linear referencing, MakeValid …);
tier 1 is done (commits `4df2255`, `a990d44`, every node with tests and a High flag, six patches seen in
the GUI); tier 2 is the next work, one tier per commit.

**`NetTopologySuite.Index` is this package's first `[ProcessNode]`.** `SpatialIndex` holds an `STRtree` and
rebuilds it only when the set of geometry *references* changes; `Query` returns **Candidates**, not
results. The contract and why it is that one and not another are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#spatialindex--query--the-first-process-node), and it
is the template for the next stateful node (prepared geometry, when a chapter needs it).

**This package is the centre of the family, and references nothing of ours.** The siblings depend
on it, never the other way:

- `D:\2026_Projects\vl-mapsui` (`VL.Mapsui`) — draws NTS geometry on a map. Depends on this package
  (so its help patches open working).
- `D:\2026_Projects\vl-geojson` (`VL.NetTopologySuite.GeoJSON`, `VL.GeoJSON` until 2026-10-18) —
  `Read GeoJSON` / `Write GeoJSON`, in this package's `NetTopologySuite.IO` category. Depends on
  this package's **0.0.3-alpha** for `GetAttribute`, so that release gates its first one.
- `D:\2026_Projects\vl-overworld` (`VL.Overworld`) — the course; declares all three.
- `VL.GIS` (`rednotfound/vvvv-gis`, local clone gone) — the retired predecessor. History only; what
  it taught is carried in `docs/RULES.md` and `docs/AUDIT.md`. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#vvvv-gis-vlgis--a-reference-not-a-constraint).

**Memory for the whole family lives here** (`~\.claude\projects\D--2026-Projects-vl-nettopologysuite\memory`);
the other three repositories' memory folders are junctions onto it since 2026-10-18.

**Only this repository's `CLAUDE.md` loads automatically.** The siblings have their own rules; read
theirs before touching them, and do not assume a rule written there applies here unless it has been
copied. That principle — *a rule transfers, a note about another repository's file does not* — is
itself a lesson those repositories paid for.

## Read this before writing any node

[`docs/RULES.md`](docs/RULES.md), carried verbatim from the siblings. Seven packaging rules, four
runtime rules, and a measured node-design section. Every one is followed by what it cost. The two
that bite hardest:

1. **`[assembly: ImportAsIs(Namespace = "VL")]` is mandatory.** Without it the package loads,
   compiles, packs and exports with zero warnings and contributes **no visible nodes** —
   indistinguishable from the package not loading. Nine VL.GIS releases shipped that way.
2. **An upstream library must be a *package in a package repository*, not just a
   `<NugetDependency>` line.** Otherwise a node whose signature names an NTS type is built with no
   working pins and every link to it is dropped; `vvvvc` exits 0 and nothing is red. **This failure
   is total here, not partial: every node in this package names an NTS type.** `build.ps1` installs
   into `deps\`.

Also: a `public static` method is a node evaluated **sixty times a second from the moment the
document opens**. `GeometryFactory` and `WKTReader` are cached for exactly this reason, and
**`SpatialIndex` is a `[ProcessNode]` for the same reason at the other end of the scale** — an
index over 100,000 geometries is not something to build sixty times a second. Prepared geometry, when
it arrives, follows that node's pattern (see ROADMAP and ARCHITECTURE).

## The one thing this package adds to NTS

**NTS geometries are not deeply immutable, and this package is the thing that makes them behave as
if they were.** Measured against 2.6.0: `factory.CreatePoint(c)` keeps the caller's `Coordinate`,
and `geometry.Coordinates[0].X = 5` moves the geometry — that property hands out live references.

In VL that is worse than in C#, because a value flowing into two branches of a patch is the same
reference with no `readonly` to stop the second branch writing through it.

So **every creation node copies coordinates on the way in, every reader node copies on the way
out** (`Defaults.CopyOf`). `MutationTests` is the regression suite and it is **negative-tested**:
removing the copying turns exactly four tests red. Confirmed 2026-08-14.

Do not "simplify" this away. It is the reason these are hand-written nodes rather than VL's
reflection nodes over `GeometryFactory`.

## Things measured, so they need not be re-derived

All against **NetTopologySuite 2.6.0** — the current latest stable, verified with
`dotnet package search`; there is no 2.7 or 3.x.

| | |
|---|---|
| `GeometryFactory.Default.SRID` | `0` — which is why the default here is 0, meaning "unset" |
| Mixing SRIDs | **never throws.** 4326 ∩ 0 returns 4326 silently. A wrong CRS gives a confident wrong answer |
| `new WKTReader().Read(...)` SRID | **`-1`**, not 0 — and `WKTReader(factory)` does *not* adopt the factory's SRID either |
| Fixing that | `WKTReader(GeometryFactory)` and `DefaultSRID` are both `[Obsolete]`; route through `NtsGeometryServices` |
| `WKTReader.FixStructure` | closes unclosed rings, and the result is valid. On, to match `LinearRing` |
| `WKTWriter` SRID output | **does not exist in 2.6.** Checked against the type's members |
| `WKTWriter()` and Z | **drops Z silently.** Needs `new WKTWriter(3)` |
| Unclosed ring | `CreateLinearRing` / `CreatePolygon` throw `points must form a closed linestring` |
| Empty in | empty out, everywhere. No throw, and an empty geometry is `IsValid` |
| `IsValidOp` | gives a reason **and** a coordinate — `Self-intersection` at `(1, 1)` |
| Z and M | both survive the default factory intact. M is a scope decision, not a limitation |
| `NameAttribute` `AttributeUsage` | **`All`** — so `[Name("Read WKT")]` is legal on a *method*. Read out of VL.Core's metadata |
| `PinAttribute` targets | Property, Parameter, ReturnValue — with a `Name` property |

The last two are new ground — neither sibling uses either attribute on a member, only on a type —
and **both are confirmed honoured**: the GUI renders the node as `Write WKT`, and the generated C#
shows the pin named `WKT` feeding the `wkt` parameter. Worth knowing, because it means node labels
and pin labels can be set exactly rather than left to how VL splits a PascalCase identifier.

Two more, learned while verifying:

| | |
|---|---|
| Fluent output pin | **confirmed**: `var Output_6 = OperationNodes.Buffer(...)` vs `var Result_7 = GeometryNodes.Area(...)`. Return type equals first parameter type → `Output`; otherwise `Result`. **Bit again 2026-11-07:** `Boundary` and `Envelope` take and return `Geometry`, so their pin is `Output`; the first links said `Result` and `vvvvc` refused with `Boundary doesn't have a pin called "Result"` — the one failure on this ladder that is loud |
| An `int` pin needs an `Integer32` IOBox | a `Float64` one fails the compile with `Float64 is no Integer32!`. Cost one round on `HowTo Buffer a geometry.vl`'s `Segments` pin |
| **A Pad's `Comment` renders as a label to the RIGHT of the box** | so an IOBox occupies far more width than its `Bounds` says, and every label collision in the first help patches came from ignoring it. Give each Pad its own row, or ~200px of clear space to its right |
| Skia `AddPoly`'s `Close` pin defaults to **True** | from SkiaSharp's `AddPoly(points, close = true)`; a polyline drawn through it needs `DefaultValue="False"` on the pin or every street closes into a zigzag. Cost one GUI round on `HowTo Find the shortest path` (2026-09-25); recipe in `docs/HELP-PATCH-STYLE.md` |
| **A collection readout stops at 3 rows and prints `...`** | the editor setting `TooltipNumberOfItemsOfCollection` (user `settings.xml`, default 3), read by the editor's `DictionaryViewer` and sequence viewers. The maintainer saw a four-attribute feature list three. The Feature viewer lists every row itself (2026-11-07); a bare Dictionary IOBox keeps the editor's grid and its cap |
| **A Spread IOBox is an editable table** | XML: a Pad typed `Spread` of `String` with `<p:Value><Item>…</Item></p:Value>`; two of them side by side were the attribute table of `Attributes (Join)`, a node built and removed on 2026-11-07. Readouts of a `Spread<T>` output list items, of an `IReadOnlyList<T>` show a type name |
| A `[Name("Attributes (Join)")]` on a method | renders as the node `Attributes (Join)`: the parenthesised version is part of the plain name string, as `Vector (Join)` is in every shipped `.vl`; `[return: Pin(Name = "Attributes")]` names the output pin. Both confirmed by `vvvvc` resolving the link (2026-11-07; the node itself was removed the same night, the measurement stands) |
| **A feature is a container, not a data model** | the maintainer's decision, 2026-11-07 night, after a day of building the other thing: `Attributes (Join)`/`(Split)` (a table in, a guess inside: `"true"` and `true` collapse) and a record bridge on branch `record-bridge` (`Feature (FromRecord)` / `Record (FromFeature)` through `IVLObject` reflection, three cuts, two help patches) were all taken out. NTS's own `Features` library is a transport object with no schema and no stated rationale; VL.Mapsui and the GeoJSON package take that object. The user keeps data in their own record and gives it a `ToFeature` operation (Split its fields, pick the geometry, `Add` the labels) - the tutorial's job, in VL.Overworld. `Feature`, `Split`, `GetAttribute` are the whole category |

### Laying out a patch

`Bounds="x,y,w,h"` **is** the layout, so edit it directly rather than dragging nodes — exact and
repeatable, where dragging is neither. Three things learned doing it:

- **Anchor each edit on a match asserted to occur exactly once, and write nothing if any anchor
  misses.** Re-running a layout script after it had already applied caught 45 stale anchors and
  correctly refused to write, instead of silently moving the wrong nodes.
- **Check overlaps arithmetically, counting the label strip.** Two of the fixes introduced a *new*
  collision that the checker caught and the eye did not.
- **A box directly below a node with several outputs gets crossed by the fan-out.** Put it on the
  node's own row instead — the links leave below the bottom edge, so that row is clear.

Then look at it in the GUI. Capture the window with **`PrintWindow`** on its handle, not a screen
grab: `SetForegroundWindow` loses the race whenever another app holds focus, and the result is a
screenshot of whatever was in front — useless, and not the user's business.

### Writing a help patch — the community's style, measured

**Read [`docs/HELP-PATCH-STYLE.md`](docs/HELP-PATCH-STYLE.md) first.** The first fifteen patches were
written as essays — a 900-pixel "One idea:" box and paragraphs of 300–600 characters — and the user
recognised at once that this is not how vvvv help reads. Measured over 389 shipped HowTos and nine
community packs: a **20pt one-line heading** (`Use Buffer!`), at most one short 9pt intro, the wired
nodes, and **9pt notes beside the nodes starting with `<`**, median 34 characters, rarely over 150.
The Explanation shows the nodes by category with one line each, like VL.IO.Redis. All fifteen were
rewritten that way on 2026-09-24. Do not reference vl-overworld for style: it is ours, and is due the
same rewrite.

### Naming a help patch

**Prefix, never a number.** The five prefixes are `Explanation` (one per library, the front door),
`HowTo`, `Reference`, `Example`, `Tutorial`, and **`Help.xml` does the ordering** — that is the
convention across the 45 packs shipped with vvvv 7.4 and both sibling repositories, and it is
already written down in [`docs/RULES.md`](docs/RULES.md).

These four started out as `01 Create a Point`, `03 Create a Polygon`, `04 Read WKT`,
`06 Buffer Geometry`, numbered after an eight-topic plan of which only four were written. The result
in the help browser was `01 03 04 06`, and **every gap reads as a broken install** — which is how
the mistake was noticed. Numbering also means adding a topic in the middle renumbers files that
other documents already link to.

So: a new patch gets a `HowTo ...` name and is appended to the right `Topic` in `Help.xml`. Scaffold it with `tools\HelpPatchGen.ps1` (intro box at 60,60 900 wide, dataflow below, 440-wide notes beside or under it - the shape all fifteen share), then compile it and READ the C#: a wrong pin name does not fail the compile, it makes that input read `default(...)`. After the GUI check the `.vl` is the truth and the scaffolding script is thrown away.
`tools\Test-VLPatch.ps1` enforces the pairing in both directions — every patch on disk must be
listed, and every link must name a file that exists, because both failures are silent.
**Every patch shows its geometry** (2026-11-07): a column of drawn Geometry IOBoxes at `x = 940`,
180 × 120, one per geometry that matters, and `A and B` together through `Cons` →
`GeometryCollection` where the patch is about two; recipe in `docs/HELP-PATCH-STYLE.md`, "Showing
the geometry". 
**The layout rule for every help patch - the maintainer's, 2026-11-08, marked very important:
the nodes the patch is about are the core.** Spread them evenly over the canvas; put each one's
own inputs and helpers around it, its drawn result right next to it, its note beside it. Never
wire one shared helper (a `Coordinate`, a `Factory`, a constant) into all of them to save a node:
every link then runs to the least important thing on the canvas. `HowTo Make a shape` broke this
(seven shapes off one `Coordinate`, drawings in a far column at `x = 940`) and is now the
reference: one cell per shape on a 3-wide grid. The maintainer says many of the other patches
have the same problem; the `x = 940` column is the old way, not the rule.

## Verification — be precise about which one you have

| | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic is right | ✅ 329 tests, ~400 ms, no network (2026-11-14) |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed, annotation boxes typed, labels not colliding | ✅ 22 documents (20 help patches, the package, the HDE companion), 66 of 66 nodes flagged for F1 |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` (`vvvvc`) | every node in a patch **resolved**, read from the generated C# | ✅ all 17 help patches, 2026-11-08; `HowTo Draw a geometry` 2026-11-11 |
| `tools\Test-Install.ps1` | the **packed** package installs from `dist\feed` with its dependencies, and every help patch inside the install compiles with that install as the only repository | ✅ 2026-09-25, first pass |
| the vvvv **NodeBrowser** | **which category a node is in** | ✅ 2026-10-17: typing `NetTopologySuite` lists the category folder `NetTopologySuite` and the package; its nodes sit under `NetTopologySuite.Geometry` … `.Network` (was `NTS` until that day) |
| the vvvv **GUI, running** | the patch computes the right value | ✅ 2026-08-14, vvvv 7.4; all 15 help patches 2026-09-24, reviewed by the maintainer 2026-09-25; the six tier-1 patches 2026-11-07 |
| **F1 on a node** | the node carries a High help flag in a patch vvvv has indexed | ✅ 2026-09-24, all 39 nodes then; the 17 since (`GetAttribute`, tier 1, `Outlines`) are flagged and audited by `Test-VLPatch`, F1 not yet pressed on them |
| **a Geometry IOBox draws** | the HDE companion registered its viewers in the editor | ✅ 2026-11-07, probes with polygon-with-hole, multipolygon, line, point, multipoint, mixed collection, lon/lat polygon, a feature with attributes, all drawn GIS-style; then every help patch got a column of drawn IOBoxes (`x = 940`) and all 16 were captured again |
| **the Geometry Editor window** | the extension runs, its window edits the selected node, the edit is saved | ✅ 2026-11-11, by the maintainer on a probe: Ctrl+Shift+W opens it, every tool, Snap, MULTI/collection output, the new WKT read back out of the saved `.vl`; the 2026-11-12 redesign tried by the maintainer ("好很多了"). Not yet by hand: Ctrl+Z on a stroke, box selection |

**The last three rows are three different claims, and this is where a false proof lives.** Learned
here, at the cost of nearly writing down a wrong conclusion:

- **`LastCategoryFullName` in a `.vl` is a hint, not the truth.** Negative-tested: set it to
  `NTS.Wrong`, recompile, and every node still resolves. So a green `vvvvc` compile proves a node
  *exists* and proves **nothing** about its category. Only the NodeBrowser does.
- **A green compile is still worth having, because it is byte-level evidence.** An unresolved node
  has its links dropped, `vvvvc` exits 0, and nothing is red — so **read the generated
  `*.vl.1.cs`**, not the exit code. The `Update` body contains the actual call chain; if a node
  vanished from it, it did not resolve.

### How to re-run the GUI check

```powershell
# vvvv must be closed. Launch, read the value, CLOSE IT - never leave it running.
.\build.ps1
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.NetTopologySuite\HowTo Buffer a geometry.vl" --package-repositories ".\dist;.\deps"
```

Headless first, because it is faster and its evidence is stronger about resolution:

```powershell
.\pack.ps1                          # the feed is what lets the export half restore
.\tools\Compile-HelpPatches.ps1     # compiles EVERY patch, then READS the generated C#
.\tools\Compile-HelpPatches.ps1 -Patch "*Buffer*" -KeepOutput   # one patch, keep the *.vl.1.cs
```

The script drops a `NuGet.config` pointing at `dist\feed` beside the generated project, so the
export's restore finds the package and the whole export runs green — the kept output has the
patch's own `.dll` in `bin\Release`. Before 2026-09-24 this file called the resulting `NU1101`
"expected, because it is the export stage after codegen". The C# was indeed already written, but
the export half never ran, so half of what a compile proves was never proved. Negative-tested the
same day: a patch with the package dependency removed fails with `Not found: Read WKT`, no C#
generated, exit code 1. Every node the patch takes from this package must be in the script's node
table, so a new node shows up there the first time a patch uses it.

## Commands

```powershell
# vvvv must be CLOSED first - it holds the staged assemblies open, and build.ps1 refuses otherwise
.\build.ps1                     # build + stage dist\
dotnet test test\VL.NetTopologySuite.Tests\VL.NetTopologySuite.Tests.csproj
.\tools\Test-VLPackage.ps1      # static package checks
.\tools\Test-VLPatch.ps1        # structural checks on every .vl - BOM, IDs, link endpoints
.\pack.ps1                      # pack into dist\feed\
.\tools\Compile-HelpPatches.ps1 # after pack: vvvvc on every help patch, then READS the generated C#
.\tools\Test-Install.ps1        # after pack: nuget install from dist\feed, then compile the INSTALLED help patches
.\tools\Open-HelpPatch.ps1 "Buffer"   # open one patch in vvvv with the right repositories (Open-HelpPatch.cmd is the picker)

# The only thing that proves a node exists:
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.NetTopologySuite\HowTo Create a point.vl" --package-repositories .\dist
```

**Two rules learned on 2026-09-24, each at a cost.** (1) The user hand-edits help patches in vvvv
between sessions and does not commit them: run `git status help/` before regenerating or checking out
any `.vl`, and treat a modified patch you did not touch as theirs — a hand-edited patch was destroyed
that way. (2) Other Claude sessions (vl-mapsui, vl-overworld, sometimes a second one here) run vvvv on
this machine; one killed this repository's window with `Stop-Process`. Every launcher here writes its
pid to `%TEMP%\vl-nettopologysuite-vvvv.pid`, closes only that pid and only with `CloseMainWindow`,
and refuses to launch while a vvvv it did not start is running. `tools\Open-HelpPatch.ps1` (and
`Open-HelpPatch.cmd`, a picker) is the launcher; `tools\HelpPatchGen.ps1` scaffolds a patch.

**Three more, carried from vl-mapsui on 2026-09-25.** (3) `dist\VL.NetTopologySuite\help` is a
**junction** to `help\VL.NetTopologySuite`, not a copy: vvvv opens the dist path for F1 and the Help
Browser, and while it was a copy a layout arranged after F1 was saved where the next build deleted
it. If `build.ps1` refuses with "edited in dist\", copy the named files into `help\` first. A patch
saved in the GUI needs no rebuild to be what F1 shows. (4) While the vvvv **this launcher started**
is open, `Open-HelpPatch` opens the next patch as another tab in it (vvvv is single-instance and
forwards the file) - so a GUI session can open, adjust and save several patches without closing
between them; the picker's "Close my vvvv" asks that pid to close and never forces it, because a
dirty tab makes vvvv ask "save changes?". (5) A link's `Ids` is a path, `src,waypoint,sink`, once a
link is bent in the GUI; `Test-VLPatch` parses it that way. (6) `CloseMainWindow` closes the editor
and **an open Help Browser window keeps the process alive** (measured 2026-09-25): a launcher that
wants its vvvv gone sends WM_CLOSE to every visible window of its own pid — still the X button,
still never a kill — which the picker and the capture script now do.

**One more, 2026-11-07.** (7) **Commit, do not push.** The maintainer decides when a branch goes
to the remote ("我都没有让你 push 到远端，你都给我 push 上去了"); a push earlier in the same session is
not permission for the next one. Commit by file name, report the commit, offer the push and stop.
The same line already covers the release tag and the nuget.org upload (docs/RELEASE.md).

**Never leave vvvv running unattended, and never start it in the background.** Launch, read the
value, close. In vvvv, having a patch open means having it running — there is no idle state.

## Repository layout

```
vl-nettopologysuite/
├── VL.NetTopologySuite.vl / .nuspec   # the package. .vl is hand-edited, never regenerated
├── VL.NetTopologySuite.HDE.vl         # editor-only companion: Geometry/Point/Feature viewers, and the Geometry Editor window (Application)
├── src/VL.NetTopologySuite/
│   ├── Defaults.cs                    # ⭐ the default factory and the defensive copying
│   ├── InputSets.cs                   # the rebuild-detection rule SpatialIndex and Network share
│   ├── GeometryNodes.Creation.cs      # NetTopologySuite.Geometry - Coordinate, Coordinate (FromVector2) .. GeometryCollection
│   ├── GeometryNodes.Shapes.cs        # NetTopologySuite.Geometry - Rectangle, Rectangle (Join Points), Circle, Ellipse, RegularPolygon, Arc, Sector
│   ├── GeometryNodes.Inspection.cs    # NetTopologySuite.Geometry - SRID, SetSRID, Area, IsValid, Bounds, Coordinates, Outlines, Envelope, Boundary, InteriorPoint, NumPoints, IsSimple …
│   ├── OperationNodes.cs              # NetTopologySuite.Operation - Buffer, ConvexHull, Reverse, four overlays, eight predicates + Relate, Distance, Nearest Points, IsWithinDistance
│   ├── IONodes.cs                     # NetTopologySuite.IO - FromWKT, ToWKT (Read WKT, Write WKT until 0.0.6)
│   ├── FeatureNodes.cs                # NetTopologySuite.Feature - Feature, Split, GetAttribute (the whole category: a feature is a container)
│   ├── SpatialIndexNode.cs            # NetTopologySuite.Index - SpatialIndex (process node)
│   ├── IndexNodes.cs                  # NetTopologySuite.Index - Query
│   ├── Network.cs                     # NetTopologySuite.Network - the retained handle type, and the scope sentence
│   ├── NetworkNode.cs                 # NetTopologySuite.Network - Network (process node)
│   ├── NetworkNodes.cs                # NetTopologySuite.Network - ShortestPath
│   ├── ViewFrameNode.cs               # NetTopologySuite.Viewer (hidden) - ViewFrame, the Geometry IOBox's view (process node)
│   ├── ViewFrame.cs                   # its arithmetic: the sticky view, grid, label, widget transform (internal)
│   ├── GeometryEditorNode.cs          # NetTopologySuite.Geometry - GeometryEditor (process node): WKT pin in, geometry out, writes the pin back
│   ├── GeometryEditorWindowNode.cs    # NetTopologySuite.Viewer (hidden) - GeometryEditorWindow, the window's canvas for the HDE Application
│   ├── GeometryEditorLayer.cs         # that canvas: toolbar, grid, drawing, mouse and keys (Skia ILayer, internal)
│   ├── SketchSession.cs               # the tools as a state machine, no Skia (internal, tested)
│   ├── GeometrySketch.cs              # a drawing as parts (point / line / polygon with holes) <-> WKT (internal, tested)
│   └── SketchView.cs                  # the window's view: geometry <-> pixels, zoom, pan, grid step (internal, tested)
├── test/VL.NetTopologySuite.Tests/    # 329 xunit tests, no network, no vvvv
├── help/VL.NetTopologySuite/          # 20 help patches + Help.xml (ordering and tags)
├── docs/AUDIT.md                      # the audit this package was designed from, and every measurement
├── docs/ARCHITECTURE.md               # why each node exists, what stays raw, the boundary
├── docs/HELP-PATCH-STYLE.md           # the community's help style, measured; help flags; the Skia recipe
├── docs/NETWORK-SCOPE-*.md            # the review that promoted NetTopologySuite.Network, and its evidence
├── docs/ROADMAP.md                    # next / later / never
├── docs/RELEASE.md                    # release checklist, what each step proves, the maintainer's open decisions
├── docs/RULES.md                      # ⭐ carried from the siblings - read before any node
├── build.ps1, pack.ps1
├── Open-HelpPatch.cmd                 # the picker: Help.xml order, Open / Close my vvvv / Normalize / Check
└── tools/                             # New-VLId, New-VLDocument, Find-Vvvv, Test-VLPackage, Test-VLPatch, Test-VLImportAttribute,
                                       # Compile-HelpPatches, Test-Install, Normalize-HelpPatches, HelpPatchGen, Open-HelpPatch(-GUI)
```

`GeometryNodes` is one `partial` class across two files so both halves land in one category without
relying on two types being allowed to share one.

## Node categories

```
category = (.NET namespace minus the "VL" prefix) + type name
```

Namespace `VL.NetTopologySuite` gives `NetTopologySuite`. `[Name("X")]` renames a type **for VL only**;
`[SkipCategory]` drops the type level entirely.

| Category | Source |
|---|---|
| `NetTopologySuite.Geometry` | `GeometryNodes.Creation.cs` + `GeometryNodes.Inspection.cs` — `[Name("Geometry")]`, one `partial` class |
| `NetTopologySuite.Operation` | `OperationNodes.cs` — `[Name("Operation")]` |
| `NetTopologySuite.IO` | `IONodes.cs` — `[Name("IO")]` |
| `NetTopologySuite.Feature` | `FeatureNodes.cs` — `[Name("Feature")]` |
| `NetTopologySuite.Index` | `SpatialIndexNode.cs` (`[ProcessNode(Name = "SpatialIndex", Category = "NetTopologySuite.Index")]`) + `IndexNodes.cs` (`[Name("Index")]`, `Query`) |
| `NetTopologySuite.Network` | `Network.cs` (the handle type) + `NetworkNode.cs` (`[ProcessNode(Name = "Network", ...)]`) + `NetworkNodes.cs` (`[Name("Network")]`, `ShortestPath`) |

**Finding a node's code: node → category → file.** The NodeBrowser (or a help patch's
`LastCategoryFullName`) gives the category; the table gives the file; inside it, a node whose name
has a space or a version is `[Name("Rectangle (Join Points)")]` on a method, otherwise the method is named like the node.
**Source files follow what vvvv's own libraries do** (VL.CoreLib, VL.ImGui, VL.Skia, VL.IO.Redis and
vl-mapsui, measured 2026-10-17): one type per file named after the type; a static class of
operations is `<Topic>Nodes.cs` (CoreLib: `SpreadNodes.cs`, `KeyboardNodes.cs`); a split of one class
is `<Class>.<Part>.cs` (CoreLib: `EnumerableNodes.Custom.cs`). The Gray Book itself says nothing about
file names — only the namespace and class decide the category — so this is convention, not rule.

**The top category was `NTS` until 2026-10-17, and the rename to `NetTopologySuite` was the user's own
experience:** they installed the package, searched the NodeBrowser for "NetTopologySuite" and found
nothing. Measured the same day over the 55 packs installed here: the category is the package name
minus `VL.` in every pack that does not join an existing category (`Mapsui`, `OpenCV`, `Audio`,
`IO.Redis`), and **no pack abbreviates its library's name** - `NTS` was the only one. The Gray
Book's design guideline says it outright: *the name of the main document of a library should hint at
the category the library introduces.* The NodeBrowser searches node names and categories only; the
C# import offers no search tags for static nodes (`ProcessNodeAttribute.Tags` covers two of 39), so
the category is the only way a user's search term can match. `RootNamespace` therefore equals the
assembly name, `VL.NetTopologySuite`; inside that namespace the bare identifier `NetTopologySuite`
resolves to *us*, so a fully qualified NTS name in code needs `global::` (two places, in
`OperationNodes.cs` and the tests). The C# class is `GeometryNodes` rather than `Geometry` because a
class named `Geometry` would shadow NTS's own `Geometry` in every signature in the assembly. A
document that references the NetTopologySuite nuget *directly* also sees the library's raw nodes
under `NetTopologySuite.Geometries.*`, one letter from our `NetTopologySuite.Geometry.*` - the same
situation VL.Mapsui has with `Mapsui.*`, and the category is what tells them apart (every summary
here also ended "Uses NetTopologySuite." until 2026-10-18; that line went with the tooltip rewrite
below, because a tooltip is not the place for it).

## The HDE companion: a Geometry draws itself in the editor

**`VL.NetTopologySuite.HDE.vl`** (repo root, shipped at the package root, staged by `build.ps1`) is
an editor-only document: it registers *viewers* with vvvv's editor, so a tooltip on any Geometry pin
and any IOBox of a Geometry draw the shape the way a GIS does - polygons filled semi-transparently
with holes cut out, lines stroked, points as markers, a lone point as a map pin - instead of an
object listing. Built 2026-11-07 after the maintainer asked for "the specialised IOBoxes
VL.Fuse has"; the mechanism, measured in the vvvv 7.4 install, is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#drawing-without-a-renderer--the-hde-companion).
The rules that matter when touching it:

- **It is the same mechanism VL.Stride, VL.Audio and VL.Fuse use**: `RegisterServices` →
  `RegisterViewer` (category `HDE.Viewers`, document `VL.HDE`) with a class implementing
  `IValueViewer<T>` whose `Update(Value, Context) → Widget`. vvvv loads a `<Package>.HDE.vl` beside
  the package document by itself and compiles it to `<Package>_HDE_Plugin` in its package-cache -
  which is what the `VL.Fuse_HDE_Plugin` folder there is. Template: CoreLib's help
  `HowTo Patch a Tooltip`.
- **`vvvvc` cannot compile it** (`VL.UI.Forms.dll couldn't be found`: VL.HDE needs the editor's own
  assemblies), so the ladder for this document is: `Test-VLPatch` (structure) → open a probe
  document with Geometry IOBoxes in vvvv and look. The probe used is a scratch file, never
  committed; `tools\HelpPatchGen.ps1` makes one in ten lines. Read `Documents\vvvv\gamma\vvvv_*.log`
  for the `.HDE.vl -> package-cache` line and any error.
- **Three viewers.** `GeometryViewer` for every Geometry: `Outlines` sorts it into Rings, Lines and
  Points; all rings are folded into ONE path by a ForEach **accumulator** (a Top border point linked
  to a Bottom one with `IsFeedback="true"`, the pattern of VL.Skia's `HowTo Compose PathEffects`),
  `SetFillType` EvenOdd cuts the holes, one `DrawPath` fills at 0.25 alpha and one strokes; lines
  are stroked; points become `Circle` markers with radius 2% of the bounds' larger side; all of it
  goes through `Group` and a `TransformSRT` into a `LayerWidget` (see the sticky view below).
  `PointViewer` for a lone Point draws a map pin (head, tip, white eye) at a fixed size, through
  the same `LayerWidget` + `TransformSRT` as `GeometryViewer` with a constant transform (an
  invisible `Rectangle` frame steered the editor's `LayerViewer` until 2026-11-08), so the pin is a
  marker in the box and not the box; the editor picks the viewer
  registered for the most specific type. `FeatureViewer` for a Feature: `Split`, the geometry through
  the editor's `AdaptiveViewer` (which dispatches by the geometry's *runtime* type, so a Point
  feature gets the pin - calling `GeometryViewer` directly drew nothing for the Tokyo Station point,
  which the maintainer found in `HowTo Attach attributes`), and **every attribute as a row of its
  own**, in the editor's own `DictionaryViewer` row shape, read out of `HDE.WidgetsAndViewers.vl`:
  a ForEach over the dictionary itself (CoreLib's `KeyValuePair` Split) → `Row` of `FixedWidthWidget`(key, X 0.7) | `VerticalLineWidget` |
  `Flexible`(value), a `HorizontalLineWidget` above each row, both cells through the editor's
  `AdaptiveViewer` (a quoted string, a right-aligned number, a checkbox, a colour swatch) → `Column`,
  under the geometry in one `Column`. The maintainer asked for exactly the editor's look and for
  every row - the rows are uncapped on purpose, and the IOBox `Configure` panel cannot take a
  row-count option from a viewer (measured: `ViewerContext` carries no settings). Not the
  editor's `DictionaryViewer`: that one takes `TooltipNumberOfItemsOfCollection` rows (3 by
  default) and prints `...` for the rest, which the maintainer found on a four-attribute feature.
  **Measured and then taken out the same day:** a package may register a viewer for a CoreLib
  generic instantiation (`Dictionary<String, Object>`), and it then wins for every IOBox of that
  type on the machine. It worked; the maintainer rejected it because the editor's grid draws typed
  cells and a geometry package should not replace it machine-wide. This package registers viewers
  for its own types only. Colours are
  Leaflet's defaults (`#3388ff`). **Y is negated**
  before `Vector (Join)`: geometry is y-up, Skia is y-down, and the first probe drew a rising line
  as falling. The maintainer's review of the first probe set the style: *a point is a pin on a map,
  not a disc; a polygon is solid, and where its holes are must be visible*.
- **`Outlines` exists because of it**: `Coordinates` concatenates a polygon's shell and holes, and a
  polyline through that drew a stray segment into the hole. Its three outputs are public and useful
  on their own (one polyline per ring or line, every point, ready for Skia or Stride).
- **The view is sticky, with a dot grid and a size label** (2026-11-08, the maintainer: a wider
  Rectangle looked lower, a Circle the same at every radius, because LayerViewer fits whatever
  it gets every frame). `ViewFrame` (`ViewFrameNode.cs` + `ViewFrame.cs`, a process node in the hidden category
  `NetTopologySuite.Viewer`) keeps the frame until the shape outgrows it or fills less than a
  third (a fresh one gives the shape 80% of the view: 1.6 first, and the maintainer saw half
  the box used), and gives `GeometryViewer` the grid as Skia `Points`, a label `4 × 2 · grid 1`
  and the `Scaling` / `Translation` that put its 4:3 view exactly onto the widget. **The viewer
  is the community's pattern, not the editor's `LayerViewer`**: `LayerWidget (Stateful)` (Size
  1.6, 1.2, the editor's own) around a `TransformSRT`, background from `TooltipPaint` → `Color`,
  as in VL.Skia's help `Example Particle Life.HDE.vl` (VL.Audio's HDE also uses `LayerWidget`);
  public nodes only. `LayerViewer` squares whatever bounds it gets, so its side strips stayed
  empty, and anything drawn into them counts toward the bounds and zooms everything out.
  **Tried and rejected the same day:** copying `LayerViewer` into the HDE with its fit set to 4:3
  - it worked, but only with `IsFriend="true"` on the VL.Skia dependency (it uses three VL.Skia
  internals: `InViewport`, `Rectangle (Join Cached)`, SkiaPaint's `Color`), and of the nine HDE
  documents shipped with vvvv 7.4 none but VL.HDE's own declares a friend; the maintainer asked
  for the usual way. Without the friend those three were `Not found`, the log said only "has
  errors" and the IOBoxes drew white - found by File → Open on the `dist` copy of the HDE
  document (the launcher shows an empty window for it) and hovering the red nodes. Seen working: an LFO-driven width moved the
  rectangle inside a still grid, so the editor keeps one viewer instance per IOBox. Three
  traps paid for on the way: `[Smell(SymbolSmell.Internal)]` makes a node *unresolvable* from
  another document (the HDE one included) - `Hidden` is the smell that only hides it; a Skia
  `Text` layer's `Size` is a Vector2 box, the font size is `SetTextSize` on the paint (`Float32
  is no Vector2!`); and vvvv recompiles the companion only when the `.HDE.vl` changes, so
  `build.ps1` now evicts `package-cache\VL.NetTopologySuite_HDE_Plugin.<version>` too. To find
  what the log's bare "has errors" means, splice the new nodes into an ordinary probe patch and
  let `vvvvc` name the error.
- Known gaps: a `Coordinate` has no viewer (the generic one lists X, Y, Z, which is fine); markers
  in a collection whose points all coincide have zero bounds and draw nothing. A pin
  default guessed for a Skia enum made the log say `The project ... has errors` while everything
  still drew - that warning line is the only error report an HDE document gets, so read it.
- The document depends on `VL.HDE`, `VL.Skia`, `NetTopologySuite` and `NetTopologySuite.Features`
  (for the `Geometry` / `Point` / `Feature` type arguments) and on this package at `0.0.0`. `VL.HDE` is *not* a nuspec dependency: every vvvv
  carries it, and an exported app never loads the companion.

## The Geometry Editor: a window of our own in the editor

**`GeometryEditor`** (2026-11-11) is a geometry drawn by hand. The maintainer's reason: nobody should
have to type WKT. Select the node, press **Ctrl+Shift+W**, and a window opens with a toolbar:
Select V, Point P, Line L, Polygon A | Undo, Redo | Snap S, Fit F. **Redesigned 2026-11-12 to be
foolproof**, after the maintainer moved a whole shape while trying to add a point. The rules come
from a survey of 15 map and vector editors:
- Select first, then edit: vertices and a "+" at every edge's middle appear only on the selected
  shape.
- Moving a whole shape needs the Move action.
- A drag needs 6 px.
- An action bar sits beside the selection, and a disabled action says why.
- A status bar shows the next step and the coordinates.
- A left drag on empty space draws a selection box (2026-11-12, the maintainer's choice; panning moved to
  the right and middle button). With a shape selected it takes that shape's points, otherwise every
  shape wholly inside it; several shapes move and delete together, several points delete together.

All of it is in `SketchSession` and `SketchSessionTests`. The maintainer: "好很多了". The window writes the drawing
back into the node's `WKT` pin, so the drawing is text in the `.vl`: saved with the patch, readable,
and still typeable. In an exported app the node outputs what its pin says. The full design is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#geometryeditor--drawing-in-a-window-of-the-editor).
The maintainer saw the whole chain work, including the saved `.vl`.

**The research behind it:** what a package may do to the editor, measured in the 7.4 install. There
are three mechanisms, and all are public:
1. `RegisterViewer`: read-only IOBox and tooltip views (ours, Fuse, Elementa, OpenCV).
2. A window from an `Application` in the `.HDE.vl` (`Command` → `WindowFactory` → `SkiaWindow`),
   the shape of VL.HDE's `Template.HDE.vl` and VL.Kairos's keyframe editor.
3. Writing into the document: `IDevSession.Current.CurrentSolution.SetPinValue(...).Confirm(...)`,
   as VL.Stride's `RenderWindow` and CoreLib's `PersistentState` do.

**Not possible:** an editable IOBox editor, because `EditorFactory` / `IValueEditor` in
`VL.UI.Forms` are internal. That is why this is a window and not a drawable IOBox.

What this cost, so it is not paid again:

- **The `Application` must sit in the document's ROOT patch, outside the `FullCategory` canvas.**
  Inside it, it is the ordinary definition `NetTopologySuite.HDE.Application`. vvvv never runs it,
  there is no menu entry, and the log shows no error. The proof that it runs is the log line
  `Starting VL.NetTopologySuite.HDE`.
- **vvvv 7.4's Quad menu has no "Extensions" item.** The Gray Book's editor-extensions page says it
  has; it is out of date. The command was found in the menus by the maintainer.
- **Pick a shortcut from the Gray Book SOURCE**, `reference/hde/keyboard-shortcuts.md` in
  github `vvvv/The-Gray-Book`. The site URL `hde/shortcuts.html` is a 404. Two tries failed first:
  - Alt+G opens Windows 11's AI assistant before vvvv sees the key.
  - Ctrl+Shift+G is vvvv's "group to process node".

  Three keys are combined with one `|` node plus an `Input 3` pin, as in `VL.UI.NewCSharp.HDE.vl`.
- **The output pins are `Result`, not `Output`.** The fluent rule above bit twice in one change, in
  the probe and in the HDE document. The HDE log said only "has errors"; `vvvvc` on the probe
  named the pin.
- **`VL.Core.Skia` needs `ExcludeAssets="runtime;native"`.** With `runtime` alone, libSkiaSharp
  for every platform still landed in `bin\runtimes`. VL.Core moved from 2025.7.2 to 2025.7.4 with
  it, in the test project too.
- **Hit-test at the real mouse, place at the snapped point.** With Snap on, nothing could be
  selected, because the snapped point was hit-tested too.
- **One left button must not mean several things by a few pixels.** The first Select tool
  selected, dragged a vertex, dragged the whole shape and (by double-click) inserted a point,
  depending on where the press landed. Fixing one case at a time broke another: "in Select mode I
  can still draw", then adding points was lost, then a whole shape moved by accident. The 2026-11-12
  redesign separates them by state (select first), by handle ("+") and by mode (Move), as every
  editor surveyed does. The design page is https://claude.ai/artifact/99NHSoZ7h2WKseabz6dFqH.
- **A `//` comment pasted inside a `///` block drops the whole doc comment** as badly formed XML.
  GeometryEditor had no tooltip summary for a day. `NodeDocsTests` now also fails on an exported
  type with no summary.
- The window needs no `vvvvc`-able document. The ladder is: tests (model, session, view) →
  `Test-VLPatch` → a probe in `scratch\` compiled by `vvvvc` → the GUI.

**The SRID pin labels the numbers (2026-11-12, step 1 of the SRID plan).** `GeometryEditor` has an
`SRID` input; the output carries it, and the window reads it through `CrsHints` (a short table of
common EPSG codes, no CRS database): lon/lat is drawn with x shrunk by cos(latitude), so a square
block looks square, and the status bar says `EPSG:4326 · degrees` and `lon` / `lat`. A code not in the
table says `units unknown`, never a guess. Step 2: VL.PROJ, when installed, answers for every code
through a service it registers. **Done 2026-11-12:** `ICrsCatalog` (hidden smell) is defined here,
VL.PROJ's AssemblyInitializer registers PROJ's catalogue in `AppHost.Global`'s services (global, because
the window and IOBox run in the HDE's own app host), and `CrsHints` asks it first and caches each answer.
This package still references nothing of ours. Seen in vvvv: SRID 2263 (not in the table) read
`EPSG:2263 NAD83 / New York Long Island (ftUS) · US survey foot` with a VL.PROJ patch open in another tab.
Step 3, a basemap in the window, is shelved (the maintainer, 2026-11-12: "先放一放").

## Node documentation is a tooltip

**Every `///` on a public member is shown to the user** — on hover in the patch and in the
NodeBrowser (The Gray Book, *writing-nodes*: summary "a one-liner", remarks "usage instructions,
warnings", param/returns "short info"). Until 2026-10-18 the family wrote design rationale there:
this package's remarks had a median of 432 characters (max 2,028, `SpatialIndex`), and a hover on
`GetAttribute` showed a 1,500-character essay. Seen on that hover: `<see cref>` renders as
**nothing**, `<para>`s run together without a space, and **a line break in the source is a line
break on screen**.

So: `<summary>` one line, present tense ("Makes a point at a coordinate."); `<remarks>` one or two
sentences a user needs — units, a warning; `<param>` / `<returns>` one short line per pin; plain
text, no `<c>`/`<b>`/`<see>`, one sentence per source line. **The reasoning moves to `//` comments
above the `///` block**, where it stays next to the code and never reaches a tooltip — every
measured fact that used to be in a `<remarks>` is still there. vvvv's own CoreLib measures summary
median 68 characters, remarks median 50. Internal members (`Defaults`, `InputSets`) keep their long
`///`: nobody hovers them.

`NodeDocsTests` enforces it on the built XML for every public member (summary 200, remarks 300,
pins 200 characters; no `see`/`para`/`list`/`code`; no break inside a sentence). It went red with
**104 findings** on the old docs, and green after. The same file is in VL.NetTopologySuite.GeoJSON
and VL.Mapsui, copied verbatim with only the anchor type changed.

## Adding a node

Answer these before writing it. If any answer is thin, it is a help patch rather than a node.

- **Is it in VL's shape, with the map's coordinates?** The maintainer's rule, 2026-11-08
  ([ARCHITECTURE](docs/ARCHITECTURE.md#nodes-in-vls-shape-coordinates-in-the-maps)): name the node
  after what the user wants to get (`FromWKT`, not `Read WKT`), take pins the way VL.Skia does
  (Position, Size, Anchor, `Vector2`, angles in cycles), and keep y **north** - `TopLeft` is the
  north-west corner. Screen coordinates are not this package's business.
- **Can a patch reach the same result by wiring existing nodes?** Then it is a help patch.
  `Disjoint` is absent for exactly this reason — it is `Intersects` plus `Not`.
- **Does it hold a resource?** Then `[ProcessNode]`, not a static method.
- **Is it a common geospatial concept that benefits from a VL-native node?** If not, leave it
  reachable through VL's raw .NET nodes. NTS is large and the node browser is not an API dump.
- **Three inputs is the target**; five means two decisions are wearing one node. Extra *outputs* are
  free — `IsValid` has three.
- **Does it copy its coordinates?** If it creates geometry from coordinates, or hands coordinates
  back, it must.
- **Are the units documented?** Distances follow the coordinates. On lon/lat, `Buffer(g, 0.001)` is
  a thousandth of a degree, and `Area` is in square degrees. Nothing warns; the doc comment must.

## Working style

- **Change one variable at a time.** Nine failed releases in the sibling came from editing the
  `.vl`, the csproj and the nuspec in one round with no idea which mattered.
- **Before believing a green result, name the mechanism by which it could have gone red.** A check
  that has never failed on known-bad input is not a check — which is why the copying is
  negative-tested rather than merely asserted.
- **Validate before committing, in a separate step.** A validator run in the same command block as
  a commit reports its failure after the push has already happened.
- **Measure NTS, do not trust prose about it.** Both siblings state that NTS is immutable, and both
  are half wrong. Two throwaway console probes settled more design questions here than reading did.
- **Copy node XML verbatim from a shipped patch** rather than composing it — and note that an XML
  comment cannot contain `--`.
- **Opening a help patch in vvvv rewrites its `NugetDependency` version to whatever is installed,
  and saving keeps it.** Run `tools\Normalize-HelpPatches.ps1` after any GUI session.
