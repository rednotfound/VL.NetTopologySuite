# Roadmap

What is here, what is next, what is deliberately absent, and what will never arrive.

The rule throughout: **an item moves from "later" to "next" when something actually needs it**, not
because NetTopologySuite has the method. Most entries below are one `public static` method and a doc
comment; the work is deciding whether they earn a node, not writing them.

---

## Now — 0.0.1-alpha, unpublished

37 nodes across `NTS.Geometry`, `NTS.Feature`, `NTS.Operation`, `NTS.IO` and `NTS.Index`. 105 tests.
Four help patches.

**2026-08-23: `NTS.Index` arrived — and with it the package's first `[ProcessNode]`.** `SpatialIndex`
builds an `STRtree` over a spread of geometries, once, and `Query` asks it for the geometries whose
*bounds* intersect a search geometry's bounds. Two nodes, by decision: STRtree only (Quadtree's
incremental-insert lifecycle is a different model and waits for a use case), no `Envelope` on any
pin (the search geometry's own extent is used), no nearest-neighbour / node-capacity / remove until
a chapter needs one. The output is named **Candidates**, not Results, because that is what
`STRtree.Query` returns — NTS documents it as *"items whose bounds intersect the given envelope"* —
and the course chapter it exists for teaches exactly that gap.

The lifecycle contract, decided before a line was written and pinned by 16 tests: **rebuild when
the set of geometry references changes** (count, or any position holding a different object) —
never when only the spread wrapper is new, and never by structural comparison. Mutating an indexed
geometry in place is unsupported and undetectable, and unreachable through this package's own nodes
(they copy on the way in). `Build()` is called explicitly, so `Indexes Built` means what it says.
Reasoning in [ARCHITECTURE.md](ARCHITECTURE.md#spatialindex--query--the-first-process-node).
**Resolves under `vvvvc`** (probe compiled from VL.Overworld's harness, 2026-08-23): the generated C#
constructs `new SpatialIndexNode()` once, in `Create`, and calls only `Update` per frame — which is
the whole point of a process node, now visible in the output; and a `Spread<Point>` fed the
`IEnumerable<Geometry?>` input without a conversion node, so covariance holds across the VL import.
**Not yet seen in the GUI**: the first consumer will be VL.Overworld's Tutorial 11.

**2026-08-22: `Nearest Points` arrived**, by exactly the rule at the top of this file: the course's
distance chapter needed to *draw* the shortest line between two geometries, not merely number it.
`DistanceOp.NearestPoints`, coordinates copied on the way out, empty-or-missing in → nothing out.
Like `NTS.Feature` below, it has not yet been seen in the GUI.

**2026-08-22: `NTS.Feature` arrived** — `Feature` and `Split`, moved from VL.Mapsui with their
tests, plus the `NetTopologySuite.Features 2.1.0` dependency. The reasoning and the field-wide
evidence are in [ARCHITECTURE.md](ARCHITECTURE.md#where-a-feature-lives). **Not yet seen in the
GUI**: the 2026-08-14 NodeBrowser verification below predates this category, so "appears under
`NTS.Feature` with working pins" is currently a claim only a vvvv session can settle.

**The two MVP paths are implemented and covered by tests:**

```text
Coordinates → LinearRing → Polygon → Buffer → Geometry     ✅ tested
WKT → Read WKT → Geometry                                  ✅ tested
```

Both produce a native `NetTopologySuite.Geometries.Geometry`, asserted as such.

### Verified in the GUI — 2026-08-14, vvvv gamma 7.4

The blocking gap is closed. `NTS` appears in the NodeBrowser with exactly three sub-categories
(**Geometry**, **IO**, **Operation**), all four help patches open with no unresolved node, and they
compute correct values — `POINT (139.7671 35.6812)`, a unit square of area 1.00 with an empty
validity reason, and a 0.25 buffer taking area 1.00 → 2.20 and intersecting back to 1.00.

Also settled, each of which had been listed as unknown:

- `[Name(...)]` on a **method** *is* honoured, and `[Pin(Name = ...)]` on a **parameter** is too.
- Fluent operations *do* get an `Output` pin; non-fluent get `Result`.
- **`LastCategoryFullName` in a `.vl` is only a hint** — negative-tested by setting it to
  `NTS.Wrong`, after which every node still resolved. A compile therefore proves a node exists and
  says nothing about its category. That distinction is now recorded in
  [CLAUDE.md](../CLAUDE.md#verification--be-precise-about-which-one-you-have).

One bug it caught: `Segments` had a `Float64` IOBox against an `int` pin, which `vvvvc` rejects with
`Float64 is no Integer32!`. Fixed.

---

## Next

In order, and each small.

**Done 2026-08-14: the help-patch layouts.** All four were re-laid-out and each checked in the GUI.
The rule that fixed them: **a Pad's `Comment` renders as a label to the right of the box, so an
IOBox occupies far more width than its `Bounds` says** — every label collision came from ignoring
that. Every Pad now has its own row or ~200px of clear space to its right.

From here **the checked-in `.vl` is the source of truth and must not be regenerated**, because that
would discard the layout. Edit `Bounds` in place, anchored on a match asserted to occur exactly
once, and re-run the overlap check.

| | |
|---|---|
| **`Explanation Overview of available nodes.vl`** | One per library, the front door — 57 of vvvv's own packs have one. Help is the teaching surface: VL.Skia ships 4 C# nodes and 98 help patches, and in libraries people learn from help runs 16–24% of node count. Four patches against 32 nodes is 12%, so this is under-served rather than done. |
| **The remaining help patches** | `HowTo Create a linestring`, `HowTo Inspect a geometry`, `HowTo Intersect two geometries`, `HowTo Test how geometries relate`. Append each to the right `Topic` in `Help.xml` — **do not number the files.** They were numbered `01 03 04 06` at first, and because only four of the eight existed, every gap read as a broken install. `Help.xml` is the only place ordering lives. |
| **The cross-package example** | `Coordinates → Polygon → Buffer → Geometry → VL.Mapsui Feature → Map`, living **outside** both repositories. A patch needing two packages cannot ship inside one whose dependencies do not guarantee the other. Precedent: `vvvv-gis\examples\Example Map with data on it.vl`. |
| **The remaining predicates** | `Touches`, `Crosses`, `Overlaps`, `Covers`. One line each, left out only to keep the first surface small. `Disjoint` stays out permanently — it is `Intersects` plus `Not`. |
| **`SymmetricDifference`** | The fourth overlay operation, omitted because it is the least used of the four. |

---

## Later

Ordered by how likely something is to need them.

| | |
|---|---|
| **WKB** | `Read WKB` / `Write WKB`, plus the hex form PostGIS uses. Genuinely in scope — it is NetTopologySuite's own IO, same as WKT — and deferred only until the core is stable. |
| **`ConvexHull`, `Simplify`** | Douglas-Peucker and topology-preserving simplification. Both common enough to expect; both waiting on a use case that says which tolerance semantics matter. |
| **M ordinates** | `CoordinateM`, `CoordinateZM`, and telling `Write WKT` to emit them. **Measured working** — M survives the default factory intact — so this is a scope decision, not a limitation. XY is stable first, then XYZ, then M. |
| **Validation detail** | `IsValid` already reports a reason and a location. NTS also offers `IsValidOp` with self-touching-ring tolerance, and `GeometryFixer` for repairing invalid geometry. `GeometryFixer` in particular would be useful and needs thought about whether silently repairing geometry is a thing a node should do. |
| **Precision tools** | Fixed-precision models, `GeometryPrecisionReducer`. `GeometryFactory` is already the way in — a `PrecisionModel` built through raw .NET nodes plugs straight into the same pin. A dedicated node waits for someone hitting a robustness problem. |
| **Prepared geometry** | `PreparedGeometryFactory` makes repeated predicates against one fixed geometry much faster, which is exactly the shape of a VL patch testing many points against one polygon every frame. **This one needs `[ProcessNode]`**, not a static method: it holds a prepared index that must be built once and rebuilt only when the geometry changes. Written as a static method it would rebuild the index sixty times a second, which is the same class of mistake as rule 8 in [RULES.md](RULES.md). |
| **Spatial indexing — `Quadtree`** | `STRtree` arrived 2026-08-23 as `NTS.Index` (see Now). `Quadtree` deliberately did not: it exists for incremental insertion into a mutable index, which is a different lifecycle from "build once, query many", and no chapter, prompt or user has asked for it. Not to be added because it is on the same NTS page. |
| **Linear referencing** | `LengthIndexedLine` and friends. Useful for animating along a path, which is a plausible vvvv thing to want. |
| **Triangulation** | Delaunay and Voronoi. Attractive for visual work, and the point where "is this geometry or is this rendering?" needs answering — the output is geometry, so probably here. |

---

## Never

Recorded as *never* so it stops being re-proposed. The reasoning is in
[ARCHITECTURE.md](ARCHITECTURE.md#what-this-will-never-contain).

| | |
|---|---|
| **Rendering, styling, layers, maps** | A different question. `VL.Mapsui` answers it, and this package must not know that maps exist. |
| **CRS transformation** | ProjNet's job, and a focused `VL.ProjNet` is where it belongs. No node here will ever imply that setting an SRID reprojects anything — because it does not. |
| **GeoJSON** | Lives in a separate NuGet package, brings a feature model that is not this package's business, and deserves its own focused package. Taking it on is the first step towards becoming a generic geospatial file-format package. |
| **A `Feature` model of our own** | No `VLFeature`, ever. The cross-package need appeared and was answered on 2026-08-22 by wrapping `NetTopologySuite.Features.Feature` — the NTS team's neutral model — as `NTS.Feature`. Wrapping theirs is what keeps us from inventing ours. |
| **Generalised GIS abstractions** | `IGISGeometry`, `IGeoObject`, `SpatialEntity`, `GISContext`, `GISDocument`. No current problem requires any of them. |
| **File formats** | Shapefile, GeoPackage, GeoParquet, GeoTIFF, STAC. Each is its own ecosystem and its own package. |
| **Mechanical coverage of NetTopologySuite** | Every class, method, constructor and overload as a node. That turns the node browser into an API dump. The rest of NTS stays reachable through VL's raw .NET nodes, which is a feature rather than a gap. |

---

## Not a roadmap item: what VL.GIS becomes

`VL.GIS 0.2.0-alpha` is on nuget.org and overlaps this package by roughly 40 nodes. Whether it is
later refactored onto this package, deprecated, turned into an umbrella, or left as a legacy
experiment is **a separate decision, to be taken after this package is stable and proven** — not
pre-solved here, and not a dependency in either direction.

This package carries no compatibility aliases and no migration infrastructure by design. See
[ARCHITECTURE.md](ARCHITECTURE.md#vvvv-gis-vlgis--a-reference-not-a-constraint).
