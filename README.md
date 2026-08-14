# VL.NetTopologySuite

**NetTopologySuite made natural inside [vvvv gamma](https://vvvv.org).** Not another GIS framework.

> **Status: early. Nothing is published, and no node has been seen in the vvvv GUI yet.**
> The table below says exactly what has and has not been shown. Read it before building on this.

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
| `dotnet test` | the arithmetic is right | ✅ **81 tests**, ~70 ms, no network |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed | ✅ 5 documents pass |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| **the vvvv GUI** | **a node actually appears, under the right category, with the right label** | ❌ **not yet run** |

**Only the GUI proves a node exists.** Nothing above it does. Concretely, these are unverified:

- That any node appears under `NTS.Geometry`, `NTS.Operation` or `NTS.IO`.
- That `[Name("Read WKT")]` on a *method* is honoured — it compiles, and `NameAttribute`'s
  `AttributeUsage` is `All`, but whether VL's importer reads it on a member rather than a type has
  not been shown. If it is ignored, the node is called something else and the help patches
  referencing `Read WKT` will grey out.
- That the output pin of a fluent operation is named `Output` rather than `Result`. The rule is
  documented in `docs/RULES.md`; which one `Buffer` actually gets has not been seen.
- That the four help patches open without a grey node in them.

Everything in the first three rows is real and repeatable. The last row is the next task.

---

## The nodes

**Thirty-two**, in three categories. No node takes more than three inputs.

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

```text
01 Create a Point        Coordinate → Point → Write WKT
03 Create a Polygon      Coordinates → LinearRing → Polygon, plus IsValid
04 Read WKT              text in, geometry out, and what SRID does
06 Buffer Geometry       units, segments, and chaining a second operation on
```

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
    ".\help\VL.NetTopologySuite\01 Create a Point.vl" --package-repositories .\dist
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
