# Roadmap

What is here, what is next, what is deliberately absent, and what will never arrive.

The rule throughout: **an item moves from "later" to "next" when something actually needs it**, not
because NetTopologySuite has the method. Most entries below are one `public static` method and a doc
comment; the work is deciding whether they earn a node, not writing them.

---

## Now — 0.0.1-alpha, unpublished

32 nodes across `NTS.Geometry`, `NTS.Operation` and `NTS.IO`. 81 tests. Four help patches.

**The two MVP paths are implemented and covered by tests:**

```text
Coordinates → LinearRing → Polygon → Buffer → Geometry     ✅ tested
WKT → Read WKT → Geometry                                  ✅ tested
```

Both produce a native `NetTopologySuite.Geometries.Geometry`, asserted as such.

### The one blocking gap

**No node has been seen in the vvvv GUI.** Everything below the GUI line in the README's
verification table passes; the GUI itself has not been run, because a vvvv session was open and
`build.ps1` correctly refuses to stage while one is.

Until that happens, three specific things are unproven:

1. That the nodes appear at all, and under `NTS.Geometry` / `NTS.Operation` / `NTS.IO`.
2. That `[Name("Read WKT")]` on a **method** is honoured by VL's importer. It compiles, and
   `NameAttribute`'s `AttributeUsage` is `All`, but neither sibling repository uses it on a member —
   only on a type. If it is ignored, node labels differ and the help patches grey out.
3. That the four fluent operations — `Buffer`, `Intersection`, `Union`, `Difference` — get an output
   pin named `Output` rather than `Result`. The rule is in [RULES.md](RULES.md); the help patches
   assume `Output`.

All three are cheap to fix and cheap to check. None can be checked any other way.

---

## Next

In order, and each small.

| | |
|---|---|
| **GUI verification** | The item above. Nothing else should start first: if node labels turn out different, the help patches need editing and there is no point writing more of them meanwhile. |
| **`Explanation Overview of available nodes.vl`** | One per library, the front door — 57 of vvvv's own packs have one. Help is the teaching surface: VL.Skia ships 4 C# nodes and 98 help patches, and in libraries people learn from help runs 16–24% of node count. Four patches against 32 nodes is 12%, so this is under-served rather than done. |
| **The remaining help patches** | `02 Create a LineString`, `05 Inspect Geometry`, `07 Intersection`, `08 Geometry Predicates` — the sequence the brief sketches. |
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
| **Spatial indexing** | `STRtree`, `Quadtree`. Same `[ProcessNode]` reasoning, more so — an index is a resource with a lifetime. |
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
| **A universal `Feature` model** | No `VLFeature`. `NetTopologySuite.Features.Feature` already exists as a neutral model if a real cross-package need appears — VL.Mapsui already uses it. |
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
