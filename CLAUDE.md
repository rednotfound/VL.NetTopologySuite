# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project overview

**VL.NetTopologySuite** wraps [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite)
as nodes for [vvvv gamma](https://vvvv.org). One package, one library — geometry creation,
inspection, spatial operations and WKT. Nothing about maps, rendering or reprojection.

**Current state (2026-08-28): 39 public nodes in six categories — `NTS.Network` (`Network` +
`ShortestPath`, an algorithm of ours, promoted from `NTS.Experimental.Network` by
`docs/NETWORK-SCOPE-PROPOSAL.md` after two consumers were rung-4 verified) is the sixth — 126
tests, 15 help patches (the `Explanation` front door and ten new `HowTo`s arrived 2026-09-24, every one seen in the GUI).** The
2026-08-14 GUI verification covered `NTS.Geometry` / `NTS.IO` / `NTS.Operation`; `NTS.Feature`
(2026-08-22) was seen in the GUI through VL.Overworld's Tutorial 08 on 2026-08-23; **`NTS.Index`
(2026-08-23) was seen the same evening through Tutorial 11**, with `Indexes Built` holding at 1 over
100,000 points. Every category has now been on a screen. Nothing is published to nuget.org. See
[Verification](#verification-be-precise-about-which-one-you-have).

**`NTS.Index` is this package's first `[ProcessNode]`.** `SpatialIndex` holds an `STRtree` and
rebuilds it only when the set of geometry *references* changes; `Query` returns **Candidates**, not
results. The contract and why it is that one and not another are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#spatialindex--query--the-first-process-node), and it
is the template for the next stateful node (prepared geometry, when a chapter needs it).

Two sibling repositories sit beside this one and are **references, not dependencies**:

- `D:\2026_Projects\vl-mapsui` (`VL.Mapsui`) — consumes NTS geometry. Composes through
  NetTopologySuite, not through this package. Neither references the other.
- `D:\2026_Projects\vvvv-gis` (`VL.GIS`) — an earlier package that already ships ~40 NTS nodes,
  published as `0.2.0-alpha`. **Deliberately not a constraint here.** The folder is no longer on
  disk (checked 2026-09-24); what it taught is carried in `docs/RULES.md` and `docs/AUDIT.md`. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#vvvv-gis-vlgis--a-reference-not-a-constraint).

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
| Fluent output pin | **confirmed**: `var Output_6 = OperationNodes.Buffer(...)` vs `var Result_7 = GeometryNodes.Area(...)`. Return type equals first parameter type → `Output`; otherwise `Result` |
| An `int` pin needs an `Integer32` IOBox | a `Float64` one fails the compile with `Float64 is no Integer32!`. Cost one round on `HowTo Buffer a geometry.vl`'s `Segments` pin |
| **A Pad's `Comment` renders as a label to the RIGHT of the box** | so an IOBox occupies far more width than its `Bounds` says, and every label collision in the first help patches came from ignoring it. Give each Pad its own row, or ~200px of clear space to its right |

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

## Verification — be precise about which one you have

| | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic is right | ✅ 126 tests, ~300 ms, no network |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed, annotation boxes typed, labels not colliding | ✅ 16 documents |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` (`vvvvc`) | every node in a patch **resolved**, read from the generated C# | ✅ all 15 help patches, 2026-09-24 |
| the vvvv **NodeBrowser** | **which category a node is in** | ✅ `NTS` → Geometry, IO, Operation |
| the vvvv **GUI, running** | the patch computes the right value | ✅ 2026-08-14, vvvv 7.4 |

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

# The only thing that proves a node exists:
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.NetTopologySuite\HowTo Create a point.vl" --package-repositories .\dist
```

**Never leave vvvv running unattended, and never start it in the background.** Launch, read the
value, close. In vvvv, having a patch open means having it running — there is no idle state.

## Repository layout

```
vl-nettopologysuite/
├── VL.NetTopologySuite.vl / .nuspec   # the package. .vl is hand-edited, never regenerated
├── src/VL.NetTopologySuite/
│   ├── Defaults.cs                    # ⭐ the default factory and the defensive copying
│   ├── GeometryNodes.Creation.cs      # NTS.Geometry - Coordinate .. GeometryCollection
│   ├── GeometryNodes.Inspection.cs    # NTS.Geometry - Area, IsValid, Bounds, Coordinates …
│   ├── OperationNodes.cs              # NTS.Operation - Buffer, overlay, predicates
│   └── IONodes.cs                     # NTS.IO - Read WKT, Write WKT
├── test/VL.NetTopologySuite.Tests/    # 126 xunit tests, no network, no vvvv
├── help/VL.NetTopologySuite/          # 15 help patches + Help.xml (ordering and tags)
├── docs/AUDIT.md                      # the audit this package was designed from, and every measurement
├── docs/ARCHITECTURE.md               # why each node exists, what stays raw, the boundary
├── docs/ROADMAP.md                    # next / later / never
├── docs/RULES.md                      # ⭐ carried from the siblings - read before any node
├── build.ps1, pack.ps1
└── tools/                             # New-VLId, Find-Vvvv, Test-VLPackage, Test-VLPatch, Compile-HelpPatches, Normalize-HelpPatches, HelpPatchGen
```

`GeometryNodes` is one `partial` class across two files so both halves land in one category without
relying on two types being allowed to share one.

## Node categories

```
category = (.NET namespace minus the "VL" prefix) + type name
```

Namespace `VL.NTS` gives `NTS`. `[Name("X")]` renames a type **for VL only**; `[SkipCategory]` drops
the type level entirely.

| Category | Source |
|---|---|
| `NTS.Geometry` | `GeometryNodes.*.cs` — `[Name("Geometry")]` |
| `NTS.Operation` | `OperationNodes.cs` — `[Name("Operation")]` |
| `NTS.IO` | `IONodes.cs` — `[Name("IO")]` |

`RootNamespace` is `VL.NTS`, deliberately decoupled from the assembly name `VL.NetTopologySuite` —
otherwise the category would be the unreadable `NetTopologySuite.Geometry.Point`. The C# class is
`GeometryNodes` rather than `Geometry` because a class named `Geometry` would shadow NTS's own
`Geometry` in every signature in the assembly.

## Adding a node

Answer these before writing it. If any answer is thin, it is a help patch rather than a node.

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
