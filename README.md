# VL.NetTopologySuite

**NetTopologySuite made natural inside [vvvv gamma](https://vvvv.org).** Not another GIS framework.

> **Status: early, but it runs.** Verified in vvvv gamma 7.4 on 2026-08-14: the nodes appear under
> `NTS.Geometry` / `NTS.IO` / `NTS.Operation`, all four help patches open and compute correct
> values. Nothing is published to nuget.org. The table below says exactly what has been shown.
> The `NTS.Feature` category (two nodes, moved here from VL.Mapsui on 2026-08-22) postdates that
> GUI run and **has not yet been seen in the NodeBrowser** — only compiled and tested.

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

The three kinds of verification prove different things, and being vague about which one you have
is how a package oversells itself:

| | proves | run |
|---|---|---|
| `dotnet test` | the arithmetic is right | ✅ **85 tests**, ~100 ms, no network |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed | ✅ 5 documents pass |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `vvvvc` headless compile | every node in a patch **resolved** — an unresolved one has its links dropped and vanishes from the generated C# | ✅ all 4 help patches |
| **the vvvv GUI** | **a node appears under the right category, with the right label, computing the right value** | ✅ **run 2026-08-14, vvvv 7.4** |

What the GUI actually showed:

- **The `NTS` category exists**, and opening it gives exactly three sub-categories: **Geometry**,
  **IO**, **Operation**.
- **`[Name]` on a *method* is honoured.** The nodes render as `Coordinate`, `Point`, `Write WKT`,
  `LinearRing`, `IsValid` — so `[Name("Read WKT")]` works on a member, not just on a type. Neither
  sibling package uses it that way, so this was new ground.
- **Correct values, not just resolved nodes.** `Create a point` outputs `POINT (139.7671 35.6812)`;
  `Create a polygon` gives a unit square with `Area` 1.00 and an empty validity `reason`;
  `Read and write WKT` reports type `Polygon`, area 1.00, SRID 0 and an identical round trip; and
  `Buffer a geometry` takes a unit square from area 1.00 to 2.20 with a 0.25 buffer — matching
  1 + 4×0.25 + ≈π×0.25² — then back to 1.00 by intersecting the buffer with the original.
- **Fluent operations do get an `Output` pin**, non-fluent get `Result`, exactly as
  [docs/RULES.md](docs/RULES.md) says. Confirmed in the generated C#:
  `var Output_6 = OperationNodes.Buffer(...)` beside `var Result_7 = GeometryNodes.Area(...)`.

Two things this run caught that nothing else would have:

- **`Segments` needed an `Integer32` IOBox, not `Float64`.** `vvvvc` refused with
  `Float64 is no Integer32!`. Fixed in `HowTo Buffer a geometry.vl`.
- **`LastCategoryFullName` in a `.vl` is a hint, not the truth.** Setting it to `NTS.Wrong` and
  recompiling — every node still resolved. So a successful compile proves a node *exists*, and
  proves nothing about which category it is in. Only the NodeBrowser does. This is why the row above
  is split in two.

The layouts were cramped when first generated, and are not any more — all four were re-laid-out and
each checked in the GUI. Still not done: **nothing is published to nuget.org**, and four of the
eight help topics are still unwritten (see [docs/ROADMAP.md](docs/ROADMAP.md)).

---

## The nodes

**Thirty-four**, in four categories. No node takes more than three inputs.

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

`Buffer` · `Intersection` · `Union` · `Difference` · `Distance` · `Intersects` · `Contains` · `Within`

### `NTS.IO`

`Read WKT` · `Write WKT`

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

Four, in `help\VL.NetTopologySuite\`. Each teaches one idea:

| Patch | teaches |
|---|---|
| `HowTo Create a point` | `Coordinate` → `Point` → `Write WKT`, and what the `Factory` pin is for |
| `HowTo Create a polygon` | `Coordinates` → `LinearRing` → `Polygon`, ring closure, and `IsValid` |
| `HowTo Read and write WKT` | text in, geometry out, the round trip, and what SRID does |
| `HowTo Buffer a geometry` | units, segments, and chaining a second operation onto the first |

**No numbers in the filenames, deliberately.** The convention across vvvv's own packs is the five
prefixes — `Explanation`, `HowTo`, `Reference`, `Example`, `Tutorial` — with `Help.xml` doing the
ordering. These were numbered `01 03 04 06` at first, and every gap read as a broken install.
`Help.xml` is now the only place order lives, and `tools\Test-VLPatch.ps1` checks that it accounts
for every patch on disk and names none that is missing.

---

## Building it

Needs the .NET 8 SDK and **vvvv gamma 7.2 or newer** (7.4 is what this was built against).
Everything else — `NuGet.exe`, `vvvvc.exe` — ships inside vvvv.

```powershell
.\build.ps1                     # build + stage dist\  (refuses to run while vvvv is open)
dotnet test test\VL.NetTopologySuite.Tests\VL.NetTopologySuite.Tests.csproj
.\tools\Test-VLPackage.ps1      # static package checks, no vvvv needed
.\tools\Test-VLPatch.ps1        # structural checks on every .vl
.\pack.ps1                      # pack into dist\feed\

# Then, the only thing that proves a node exists:
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.NetTopologySuite\HowTo Create a point.vl" --package-repositories .\dist
```

`build.ps1` refuses to run while vvvv is open, deliberately: a running vvvv holds the staged
assemblies, and would not pick up the change anyway.

---

## Documentation

| | |
|---|---|
| [docs/AUDIT.md](docs/AUDIT.md) | the audit this package was designed from, including everything measured against NTS |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | why each node exists, what stays raw, and the decisions behind both |
| [docs/ROADMAP.md](docs/ROADMAP.md) | what is next, what is deliberately absent, and what will never be here |
| [docs/RULES.md](docs/RULES.md) | ⭐ the packaging and runtime rules, carried from the sibling repositories. **Read before adding a node.** |

`docs/RULES.md` is not general advice. Every rule in it is followed by what it cost — nine
releases that installed and contributed zero nodes, and a home network taken down by a node written
as a `public static` method. It transfers; a link to another repository's copy would not.

---

## Licence

MIT. NetTopologySuite is BSD-3-Clause and is declared as a dependency rather than redistributed.
