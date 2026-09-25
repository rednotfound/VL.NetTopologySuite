# VL.NetTopologySuite

**NetTopologySuite made natural inside [vvvv gamma](https://vvvv.org).** Not another GIS framework.

> **Status: `0.0.1-alpha` on nuget.org since 2026-09-26 — a prerelease, early, and it works.**
> 39 nodes in six categories, 126 tests, 15 help patches, every node opens one on **F1**. Every
> category has been seen running in vvvv gamma 7.4, all fifteen help patches were reviewed by hand
> in the editor, and the published package was installed back from nuget.org alone, its fifteen help
> patches compiled from that install and one of them run in vvvv from it. The table below says
> exactly what has been shown; the release process and its rules are in
> [docs/RELEASE.md](docs/RELEASE.md).
>
> ```text
> nuget install VL.NetTopologySuite -pre
> ```
>
> in vvvv's command line, or the Package Manager. `-pre` because every version of this package is a
> prerelease for now; the version moves in step with VL.Mapsui and the rest of the family.

---

## The mental model

```text
Coordinates
    ↓
Geometry
    ↓
Spatial operation
    ↓
Geometry
```

Every node hands back a **native NetTopologySuite type** — `Point`, `LineString`, `Polygon`,
`Geometry` — never a wrapper of our own. So the output of any operation is the input to the next
one, and a geometry made here goes straight into anything else that speaks NTS:

```text
VL.NetTopologySuite
        ↓
   NTS Geometry
        ↓
other packages, such as VL.Mapsui
```

Neither package depends on the other. They share the library, not each other.

---

## What is verified, and what is not

The kinds of verification prove different things, and being vague about which one you have is how
a package oversells itself:

| | proves | run |
|---|---|---|
| `dotnet test` | the arithmetic is right | ✅ **126 tests**, ~300 ms, no network |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed, labels do not collide, every node has an F1 flag | ✅ 16 documents, 39 of 39 nodes flagged |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` (`vvvvc`) | every node in a patch **resolved** — an unresolved one has its links dropped and vanishes from the generated C#, so the script reads the C# | ✅ all 15 help patches |
| `tools\Test-Install.ps1` | the **packed** package installs from a feed with its dependencies, and every help patch inside it compiles with that install as the only package repository | ✅ 2026-09-25 |
| **install from nuget.org** | the published package resolves with its dependencies from nuget.org alone; its 15 shipped help patches compile from that install; `HowTo Find the shortest path` runs in vvvv from it | ✅ 2026-09-26, `0.0.1-alpha` |
| **the vvvv GUI** | **a node appears under the right category, with the right label, computing the right value** | ✅ every category; all 15 help patches seen 2026-09-24, reviewed by hand 2026-09-25 |
| **F1 on a node** | the node opens its help patch | ✅ all 39 nodes, 2026-09-24 |

What the GUI showed, category by category:

- **`NTS.Geometry` / `NTS.IO` / `NTS.Operation`** (2026-08-14): `Create a point` outputs
  `POINT (139.7671 35.6812)`; `Create a polygon` gives a unit square with `Area` 1.00 and an empty
  validity reason; `Read and write WKT` round-trips a polygon at SRID 0; `Buffer a geometry` takes
  area 1.00 to 2.20 with a 0.25 buffer, matching 1 + 4×0.25 + ≈π×0.25², and back to 1.00 by
  intersecting with the original.
- **`NTS.Feature`** (2026-08-23, through VL.Overworld's Tutorial 08, and 2026-09-24 in
  `HowTo Attach attributes to a geometry`).
- **`NTS.Index`** (2026-08-23, through Tutorial 11): 100,000 points, a mouse-driven query polygon,
  `Indexes Built` holding at 1 while the mouse moved, indexed and brute-force counts equal.
- **`NTS.Network`** (2026-08-23 through Tutorial 13; 2026-09-24 in `HowTo Find the shortest path`,
  which draws its town with VL.Skia): `Networks Built` stays at 1 while From and To move, `Found`
  turns False on an island, the teal path follows the streets.
- **`[Name]` on a *method* is honoured**, so the nodes render as `Read WKT`, `Write WKT`,
  `Nearest Points`. Fluent operations get an `Output` pin, the rest get `Result` — confirmed in
  the generated C#.

Two things the tooling caught that the eye would not:

- **`LastCategoryFullName` in a `.vl` is a hint, not the truth.** Set it to `NTS.Wrong`,
  recompile, and every node still resolves. A green compile proves a node *exists* and nothing
  about its category. Only the NodeBrowser does. That is why the GUI has its own row above.
- **A green `vvvvc` exit code proves nothing either.** An unresolved node has its links dropped
  silently. `Compile-HelpPatches.ps1` therefore reads the generated `*.vl.1.cs` and looks for each
  node's call.

---

## The nodes

**Thirty-nine**, in six categories. No node takes more than three inputs, except where a fourth
is a default you will rarely touch.

### `NTS.Geometry` — making geometry

| Node | in → out |
|---|---|
| `Coordinate` | X, Y → `Coordinate` |
| `CoordinateZ` | X, Y, Z → `CoordinateZ` |
| `Split` | `Coordinate` → X, Y, Z |
| `GeometryFactory` | SRID → `GeometryFactory` |
| `Point` | `Coordinate` → `Point` |
| `LineString` | spread of `Coordinate` → `LineString` |
| `LinearRing` | spread of `Coordinate` → `LinearRing` *(closes the ring for you)* |
| `Polygon` | `LinearRing` shell, spread of `LinearRing` holes → `Polygon` |
| `MultiPoint` | spread of `Point` → `MultiPoint` |
| `MultiLineString` | spread of `LineString` → `MultiLineString` |
| `MultiPolygon` | spread of `Polygon` → `MultiPolygon` |
| `GeometryCollection` | spread of `Geometry` → `GeometryCollection` |

### `NTS.Geometry` — reading it back

| Node | in → out |
|---|---|
| `GeometryType` | `Geometry` → the OGC name |
| `IsEmpty` | `Geometry` → bool |
| `SRID` | `Geometry` → int |
| `IsValid` | `Geometry` → bool, **reason**, **location** |
| `Area` | `Geometry` → float |
| `Length` | `Geometry` → float |
| `Centroid` | `Geometry` → `Point` |
| `Bounds` | `Geometry` → MinX, MinY, MaxX, MaxY |
| `Coordinates` | `Geometry` → spread of `Coordinate` |
| `Geometries` | `Geometry` → spread of `Geometry` |

### `NTS.Feature` — attaching data to geometry

| Node | in → out |
|---|---|
| `Feature` | `Geometry`, Dictionary of attributes → `Feature` |
| `Split` | `Feature` → `Geometry`, Dictionary of attributes |

The type is `NetTopologySuite.Features.Feature` — the NTS team's own neutral model, not a wrapper
of ours. These two moved here from VL.Mapsui on 2026-08-22, because a feature has to be
constructible without a map engine: VL.GeoJSON writes them, VL.Mapsui draws and picks them, and a
patch can make one by hand. The reasoning and the field-wide evidence are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#where-a-feature-lives).

### `NTS.Operation`

`Buffer` · `Intersection` · `Union` · `Difference` · `Distance` · `Nearest Points` · `Intersects` · `Contains` · `Within`

### `NTS.IO`

`Read WKT` · `Write WKT`

### `NTS.Index` — asking many geometries a question without asking each one

| Node | in → out |
|---|---|
| `SpatialIndex` | spread of `Geometry` → `STRtree`, **Count**, **Indexes Built** |
| `Query` | `STRtree`, search `Geometry` → **Candidates** (spread of `Geometry`), **Candidate Count** |

**The package's first process node** (2026-08-23). An index is built once, held, and rebuilt only
when the *set of geometry references* changes — never when only the spread wrapper is new, and
never by comparing coordinates. **Watch Indexes Built: it should reach 1 and stay.** If it climbs
every frame, the geometries upstream are being re-created every frame and the index is doing
nothing for you — `Read WKT` upstream does exactly that, and the help patches put it in a `Cache`
region for that reason.

**Candidates are not results.** `Query` returns the geometries whose *bounding boxes* intersect the
search geometry's bounding box — NTS's words: *"items whose bounds intersect the given envelope"* —
and never looks at the geometry itself. Finish with the exact predicate you meant (`Intersects`,
`Contains`, `Distance`) on the candidates. The index did not answer your question; it narrowed who
gets asked. Reasoning and the lifecycle contract in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#spatialindex--query--the-first-process-node).

### `NTS.Network` — close does not mean reachable

| Node | in → out |
|---|---|
| `Network` | spread of `LineString` → `Network`, **Node Count**, **Edge Count**, **Networks Built** |
| `ShortestPath` | `Network`, From `Point`, To `Point`, Max Snap Distance → `Path` (`LineString`), **Length**, **Found**, **From Snap Distance**, **To Snap Distance** |

NetTopologySuite ships no shortest-path capability, so this is an algorithm of ours. It was built
as `NTS.Experimental.Network` in 2026-08 for VL.Overworld's Tutorial 13 and **promoted to
`NTS.Network` on 2026-08-28** after a scope review with two real consumers
([docs/NETWORK-SCOPE-PROPOSAL.md](docs/NETWORK-SCOPE-PROPOSAL.md)).

Connectivity is **exact shared endpoints** — no tolerance, no auto-noding, a crossing is not a
junction, an interior vertex is shape. Cost is length, edges are undirected, From/To snap to the
nearest node within `Max Snap Distance` and the snap distances are pins. `Found = false` is a
result. One sentence of scope, a long non-scope list in [docs/ROADMAP.md](docs/ROADMAP.md), and no
promise of a routing library.

Everything else NetTopologySuite offers — and it is a large library — stays reachable through VL's
raw .NET nodes. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#what-stays-raw).

---

## Four things that will bite you

Each is measured, not inherited from documentation.

**1. NTS geometries are not as immutable as everyone says.** Operations return new geometries, but
coordinate storage is *shared and writable*: NTS's own `factory.CreatePoint(c)` keeps the caller's
`Coordinate`, and `geometry.Coordinates[0].X = 5` moves the geometry. **This package copies on the
way in and on the way out**, so it cannot happen through these nodes. It is the main reason they
exist rather than using VL's reflection nodes on `GeometryFactory` directly.

**2. Units follow the coordinates, and nothing warns you.** `Buffer(geometry, 0.001)` on longitude
and latitude is a thousandth of a *degree* — about 111 m near the equator, less further from it,
and not a constant number of metres anywhere. `Area` on lon/lat is in *square degrees*. Project to
a metric reference system before doing metric work.

**3. An SRID is a label, not a transformation.** Setting it to 4326 does not reproject anything and
does not check that the numbers are longitude and latitude. The default is **0**, meaning unset,
which is NTS's own default and the honest one for a library that has not seen your data. Worse:
mixing SRIDs **never throws** — a 4326 geometry intersected with an SRID 0 one returns 4326 without
complaint — so a wrong CRS gives a confident wrong answer. Reprojection belongs in a different
package.

**4. Coordinate order is X first.** Longitude then latitude, which is the opposite of how they are
usually spoken, and the most common bug in this domain.

---

## Help patches

Fifteen, in `help\VL.NetTopologySuite\`, in the order the Help Browser shows them. Press **F1** on
any node of this package to open the one that teaches it.

| Topic | Patch |
|---|---|
| Start here | `Explanation Overview of available nodes` — every node, one line each |
| Making geometry | `HowTo Create a point` · `HowTo Create a linestring` · `HowTo Create a polygon` · `HowTo Group geometries` · `HowTo Work with Z` |
| Looking at geometry | `HowTo Inspect a geometry` |
| Text in, geometry out | `HowTo Read and write WKT` |
| Spatial operations | `HowTo Buffer a geometry` · `HowTo Combine two geometries` · `HowTo Test how geometries relate` · `HowTo Measure distance` |
| Features | `HowTo Attach attributes to a geometry` |
| Many geometries | `HowTo Query many geometries fast` |
| Networks | `HowTo Find the shortest path` — draws its town and the path with VL.Skia |

They are written the way the vvvv community writes help: a one-line heading, the wired nodes, and
short notes beside them — a style measured over 389 shipped HowTos and nine community packs, in
[docs/HELP-PATCH-STYLE.md](docs/HELP-PATCH-STYLE.md).

**No numbers in the filenames, deliberately.** The convention across vvvv's own packs is the five
prefixes — `Explanation`, `HowTo`, `Reference`, `Example`, `Tutorial` — with `Help.xml` doing the
ordering. These were numbered `01 03 04 06` at first, and every gap read as a broken install.
`Help.xml` is the only place order lives, and `tools\Test-VLPatch.ps1` checks that it accounts
for every patch on disk and names none that is missing.

---

## Building it

Needs the .NET 8 SDK and **vvvv gamma 7.2 or newer** (7.4 is what this was built against).
Everything else — `NuGet.exe`, `vvvvc.exe` — ships inside vvvv.

```powershell
.\build.ps1                     # build + stage dist\  (refuses to run while vvvv is open)
dotnet test test\VL.NetTopologySuite.Tests\VL.NetTopologySuite.Tests.csproj
.\tools\Test-VLPackage.ps1      # static package checks, no vvvv needed
.\tools\Test-VLPatch.ps1        # structural checks on every .vl, label arithmetic, F1 flags
.\pack.ps1                      # pack into dist\feed\
.\tools\Compile-HelpPatches.ps1 # vvvvc on every help patch, then READS the generated C#
.\tools\Test-Install.ps1        # install the packed package from dist\feed like a user would

# Open a help patch in vvvv with the right package repositories (or double-click Open-HelpPatch.cmd):
.\tools\Open-HelpPatch.ps1 "Buffer"
```

`build.ps1` refuses to run while vvvv is open, deliberately: a running vvvv holds the staged
assemblies, and would not pick up the change anyway. `dist\VL.NetTopologySuite\help` is a junction
to the repository's help folder, so a patch saved in vvvv is the one git sees.

To use the working tree instead of the published package, point vvvv at the staged folder:
`vvvv.exe MyPatch.vl --package-repositories <repo>\dist;<repo>\deps`. The working version is
always one ahead of the published one (`0.0.2-alpha` while `0.0.1-alpha` is on nuget.org), so the
two can never be mistaken for each other in a NuGet cache.

---

## Documentation

| | |
|---|---|
| [docs/RULES.md](docs/RULES.md) | ⭐ the packaging and runtime rules, carried from the sibling repositories. **Read before adding a node.** |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | why each node exists, what stays raw, and the decisions behind both |
| [docs/AUDIT.md](docs/AUDIT.md) | the audit this package was designed from, including everything measured against NTS |
| [docs/HELP-PATCH-STYLE.md](docs/HELP-PATCH-STYLE.md) | how vvvv help patches are written, measured from shipped packs, and how F1 finds them |
| [docs/NETWORK-SCOPE-PROPOSAL.md](docs/NETWORK-SCOPE-PROPOSAL.md) | the review that promoted the network from experimental, and its [evidence](docs/NETWORK-SCOPE-EVIDENCE.md) |
| [docs/ROADMAP.md](docs/ROADMAP.md) | what is next, what is deliberately absent, and what will never be here |
| [docs/RELEASE.md](docs/RELEASE.md) | the release checklist, what each step proves, and the decisions still open |

`docs/RULES.md` is not general advice. Every rule in it is followed by what it cost — nine
releases that installed and contributed zero nodes, and a home network taken down by a node written
as a `public static` method. It transfers; a link to another repository's copy would not.

---

## Licence

MIT. NetTopologySuite is BSD-3-Clause and is declared as a dependency rather than redistributed.
