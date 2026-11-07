# Roadmap

What is here, what is next, what is deliberately absent, and what will never arrive.

The rule throughout: **an item moves from "later" to "next" when something actually needs it**, not
because NetTopologySuite has the method. Most entries below are one `public static` method and a doc
comment; the work is deciding whether they earn a node, not writing them.

---

## Now — 0.0.4-alpha on nuget.org, 0.0.5-alpha in the tree

**2026-11-07: tier 1 landed in the tree for `0.0.5-alpha`** - the 15 one-line `Geometry` nodes
listed under "What the name obliges", 150 tests, every node in a help patch with a High flag (five
patches extended below their existing layouts, `HowTo Wrap points in a hull` new for `ConvexHull`),
all six seen in the GUI computing the values their notes name. `Geometry` coverage 20 → 35 of 64
(55%); predicates 8 named of 9 plus `Relate`; overlays 4 of 4. Not published yet; tier 2 is next.

**2026-11-07, later: a Geometry draws itself in the editor.** `VL.NetTopologySuite.HDE.vl` registers
`GeometryViewer` and `PointViewer` with vvvv's viewer registry (the mechanism behind VL.Fuse's drawn
IOBoxes, measured in the install; ARCHITECTURE "Drawing without a renderer"), so a tooltip on a
Geometry pin or an IOBox of one shows it the way a GIS would: filled polygons with holes cut out,
stroked lines, point markers, a map pin for a lone point; a Feature shows its geometry with the
attribute table under it. `Outlines` (node 56, three outputs) came out of it. Seen in a probe:
polygon with hole, multipolygon, line, point, multipoint, mixed collection, lon/lat polygon, a
feature with two attributes. **Then every help patch got a column of drawn IOBoxes** (the
maintainer: now that the editor can draw, the patches should show what they talk about): the
overlays side by side, A and B in one picture for the predicates and the distance, the gap as a
line, the town and the path, the feature with its table, a three-item gallery on the front door. One thing the captures showed that the drawn boxes cannot fix: a readout of `Geometries`' or
`Outlines`' output prints `System.Collections.Generic.List`1[...]`, because an `IReadOnlyList` is
not a VL `Spread` to the editor's viewers, while `Query`'s result lists its items. Returning
`Spread<T>` from those nodes (VL.Core's own type) would give them the same list view - a small,
separate change, noted here rather than slipped in. (`Attributes (Split)`, the same evening,
returns `Spread<String>` and its readouts list; `Geometries` and `Outlines` are still to convert.)

**2026-11-07, evening: attributes as a table, and every row shown.** The maintainer asked whether
the Dictionary was the right model for a feature's attributes and for a table-like way to enter
them ("添加起来很麻烦啊，不能像表格一样添加"). Surveyed and decided (ARCHITECTURE, "Attributes as a
table"): the model stays, `Attributes (Join)` reads two Spread IOBoxes as a table with CSV-style
type inference, `Attributes (Split)` is the inverse - nodes 57 and 58, nine tests, both in
`HowTo Attach attributes` with High flags and on the front door. Then the second complaint, "even
with four pairs the IOBox shows three": the editor's `DictionaryViewer` stops at the setting
`TooltipNumberOfItemsOfCollection` (default 3). `FeatureViewer` now lists every row itself, each
value cell through the editor's `AdaptiveViewer`. An `AttributesViewer` for a bare
`Dictionary<String, Object>` was registered and seen working - a package may register a viewer for
a CoreLib generic type, machine-wide - and the maintainer had it taken out the same evening: the
editor's grid (typed cells, a checkbox for a boolean) is the better viewer, and the first cut's
text-only cells were a step back. `Attributes (Split)` hands values out typed for the same reason.
Seen in the GUI on the Attach-attributes patch.

**Next direction, the maintainer's (2026-11-07, late):** attributes with a schema. A GIS feature is
a row of a table whose columns are defined first; in vvvv that is a Record, and `Spread<Record>`
is the dataset. Nodes to explore on a branch: `Feature (FromRecord)` (any record's fields become
attributes, a Geometry field the geometry) and `Record (FromFeature)` (the inverse, with defaults
and Success), generic over the user's record through VL's `IVLObject` / `IVLTypeInfo` reflection -
to be measured with a probe before anything is designed on it. Dictionary and `Attributes (Join)`
stay as the schema-less path.

**2026-11-07, night, branch `record-bridge`: built and seen, twice.** First with the geometry as
its own pin; then, at the maintainer's request the same night ("feature 本身就是 record … 只有一个
geometry"), the record IS the feature: a Geometry field is the shape, the rest attributes, the rule on
the node and loud when broken. `Feature (FromRecord)` and `Record (FromFeature)` (ARCHITECTURE,
"The record is the feature"), 23 tests against an editor-shaped double, `HowTo Use your own record
as a feature` with a `Station` record defined in the patch - `Station: Geometry is the shape, 3
attributes`, `4 of 4 fields filled`, both Success true, generated C# shows
`FeatureFromRecord<Station_R>`. The pad-typed-as-feature idea was dropped the same night (a pad can
already be typed `Feature` or `Station`; an interface would save one node and cost a layer -
ARCHITECTURE). `HowTo Build a feature three ways` (patch 18) mirrors the `Add` chain, the table and the
record column by column, at the maintainer's request that neither be harder to remember.
**Later the same night the maintainer moved the one-geometry rule from the record to the door:**
a feature in storage may carry several geometries (outline, entrance, centre; PostGIS and OGR do),
only the exchange formats and the map take one, so `Geometry Field` on both nodes names the shape,
several-and-unnamed is refused with the candidates, and the other geometries ride as attributes
(ARCHITECTURE, point 1 of "The record is the feature"); `HowTo Use your own record as a feature`
got a second half that shows it - a `Building` with Outline and Entrance, three doors, the way back,
seen in vvvv. Feature stays in this package: NTS ships
`NetTopologySuite.Features` itself, VL.Mapsui's FeatureLayer and Pick and the GeoJSON package all
take the NTS `Feature`, so it is the currency between the three; the `NetTopologySuite.Feature`
category keeps it apart, and a split into its own package later would not rename a node.
Left for the maintainer: a walk through the three feature patches in vvvv, then merge or not,
then `0.0.5-alpha`. On the front door as one note line, not node rows: a generic node
with nothing connected has no type argument and `vvvvc` reports it as unresolved. Not on `main`: the maintainer opened the branch to see how far the
idea goes, and merging is their call.

**2026-10-18, evening: `0.0.4-alpha` released** - the tooltip rewrite alone, one change per release,
and a fourth walk through the process at the maintainer's suggestion, for practice. Browser upload
by the maintainer; install-back from nuget.org alone green (the first run lost one patch to a
one-off Stride assembly-processor error in the export step, which did not reproduce - see
RELEASE.md). The tree is at `0.0.5-alpha`, which is where tier 1 of the coverage plan lands.

## Before that: 0.0.3-alpha on nuget.org, 0.0.4-alpha in the tree

**2026-10-18: `0.0.3-alpha` released** - `GetAttribute`, and the version VL.NetTopologySuite.GeoJSON
depends on, so that package can now publish its first. Browser upload by the maintainer, installed
back from nuget.org alone. The tree moved to `0.0.4-alpha`.

**2026-10-18, in the tree for `0.0.4-alpha`: tooltips.** Every public `///` rewritten to what a hover
can carry (one-line summary, a sentence or two of remarks, a line per pin); the rationale moved to
`//` comments. `NodeDocsTests` holds it — 104 findings before, none after; 134 tests. CLAUDE.md,
"Node documentation is a tooltip". `0.0.3-alpha` on nuget.org shows the old essays; `0.0.4-alpha` shows the short ones.

## Before that: 0.0.2-alpha on nuget.org, 0.0.3-alpha in the tree

**2026-10-17: `0.0.2-alpha` released** the same day the category rename was made - browser upload by
the maintainer, nuget.org validated it in about ten minutes, installed back from nuget.org alone.
The nodes are under `NetTopologySuite.*` for every new install; the tree is at `0.0.3-alpha`.

**2026-10-18, in the tree for `0.0.3-alpha`: `GetAttribute`**, the 40th node, in
`NetTopologySuite.Feature` — moved from VL.GeoJSON's `GetProperty` when that package became
VL.NetTopologySuite.GeoJSON and took a dependency on this one (ARCHITECTURE, "What the decision
costs"). 133 tests, negative-tested (drop the `Exists` guard and the missing-name test goes red);
High F1 flag in `HowTo Attach attributes to a geometry`. **VL.NetTopologySuite.GeoJSON cannot be
published before a VL.NetTopologySuite carrying this node is.**

## The day before: 0.0.1-alpha on nuget.org, 0.0.2-alpha in the tree

39 public nodes across `NetTopologySuite.Geometry`, `NetTopologySuite.Feature`, `NetTopologySuite.Operation`, `NetTopologySuite.IO`, `NetTopologySuite.Index` and
`NetTopologySuite.Network`. 126 tests. Fifteen help patches - the `Explanation` front door and fourteen `HowTo`s, ten of them written 2026-09-24 with `tools\HelpPatchGen.ps1` and every one seen in the GUI.

**2026-09-26: released.** `0.0.1-alpha` was published by the maintainer through nuget.org's browser
upload, installed back from nuget.org alone with both dependencies, its fifteen help patches
compiled from that install and `HowTo Find the shortest path` run in vvvv from it. The working
version is `0.0.2-alpha`. The paragraph below is the state the day before, kept as the record.

**2026-09-25: ready to release, and not released.** The node surface has not moved since
2026-08-28. The maintainer reviewed every help patch by hand in vvvv (2026-09-24/25, eleven adjusted
and committed), every node opens its patch on F1, and `tools\Test-Install.ps1` passed for the first
time: the packed package installs from `dist\feed` with both dependencies and every shipped help
patch compiles from the installed copy. The version is **`0.0.1-alpha`, a prerelease, in step
with VL.Mapsui and the rest of the family** (the maintainer's decision, 2026-09-25 — the earlier
`0.1.0-alpha` idea is withdrawn). What remains is in [RELEASE.md](RELEASE.md): three decisions that
are the maintainer's (who pushes, the description's first line, `<readme>`), then the maintainer
pushes.

**2026-08-28: the network was PROMOTED to `NetTopologySuite.Network`** — `Network` (was `BuildNetwork`) and
`ShortestPath`, with one new pin, `Max Snap Distance` — by the Network Package Scope Proposal
([NETWORK-SCOPE-PROPOSAL.md](NETWORK-SCOPE-PROPOSAL.md)), the review the paragraph below demanded.
`Nearest Node` stays unbuilt: both consumers managed without it, and it is the first candidate when
one cannot. The history, kept because the reasoning still governs what may be added:

**2026-08-23: an EXPERIMENTAL network — `BuildNetwork` and `ShortestPath` — under
`NetTopologySuite.Experimental.Network`, and deliberately not under `NetTopologySuite.Network`.** Built for VL.Overworld's
Tutorial 13 (*close does not mean reachable*). The distinction from `NetTopologySuite.Index` is the reason for the
category name: NTS owns STRtree, so `SpatialIndex` exposes NetTopologySuite; NTS ships **no**
shortest-path capability (its `PlanarGraph` is a framework for algorithm authors), so this is an
algorithm of ours, and an algorithm of ours in a package named after a library is a scope question
that a chapter alone does not settle. **Promotion to a permanent public surface waits for evidence**:
the chapter working, the abstraction that emerged from it, and at least two further genuine consumers
wanting the same model — then a separate Network Package Scope Proposal (location, identity, types,
surface, non-scope, relationship to NTS, whether our algorithms belong in this package at all).

Its scope is one sentence, and is asserted by the tests: *an undirected spatial network built from
EXPLICITLY connected LineStrings in a local Cartesian space, with geometric length as cost and
Dijkstra as the path algorithm.* Connectivity is exact shared endpoints — no tolerance, no automatic
noding, a crossing is not a junction, an interior vertex is shape not a node. From/To are Points
snapped to the nearest node with the snap distances exposed. **A maximum snap distance was the first
thing a consumer wanted (VL.Overworld `Prompt Grow a town`, 2026-08-28: with unbounded snapping a connected
network can never answer `Found = false`, so the chapter seeded two components instead) — and the scope
proposal added it the same day: `Max Snap Distance`, default infinity, refusal still reported.** `Found = false` is a first-class
result. The path keeps each edge's original geometry. Same rebuild contract as `SpatialIndex`
(shared `InputSets`): closing a bridge is a new collection and one honest rebuild.

**Explicitly out of scope, and this is not a promise of a routing library:** one-way edges, turn
restrictions or penalties, speeds, travel time, custom costs, road classes; A*, heuristics,
k-shortest paths, isochrones, contraction hierarchies; nearest-point-on-edge snapping, edge
splitting, tolerance snapping, map matching; automatic noding, topology repair, fuzzy endpoints; OSM
import and OSM semantics (`layer`, `level`, access); Z-aware connectivity; a generic graph framework
or mutable graph API; geodesic weights or CRS transformation.

**Seen in the GUI the same evening** through Tutorial 13: 200 m straight against 800 m by the streets,
`Found` False and the path gone when the bridge closes, `Networks Built` 1 → 2 → 3, snap distance 0
on a corner. The four pieces of evidence the scope review asked for — the chapter, the abstraction
that emerged, the API that now looks useful, two plausible consumers — are in
[NETWORK-SCOPE-EVIDENCE.md](NETWORK-SCOPE-EVIDENCE.md). It is evidence, not a decision.

**2026-08-23: `NetTopologySuite.Index` arrived — and with it the package's first `[ProcessNode]`.** `SpatialIndex`
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
**Seen in the GUI the same evening**, through VL.Overworld's Tutorial 11 — 100,000 points, a mouse-driven
query polygon, `Indexes Built` **holding at 1** while the mouse moved (the lifecycle contract, observed
live), `Candidates` ≥ `Accepted`, and the indexed and brute-force `Accepted` counts equal. Category
`NetTopologySuite.Index` appears with both nodes. 8 fps with the brute-force loop alongside; the drawing of 100k
points is not the cost.

**2026-08-22: `Nearest Points` arrived**, by exactly the rule at the top of this file: the course's
distance chapter needed to *draw* the shortest line between two geometries, not merely number it.
`DistanceOp.NearestPoints`, coordinates copied on the way out, empty-or-missing in → nothing out.
Seen in the GUI 2026-09-24 in `HowTo Measure distance`.

**2026-08-22: `NetTopologySuite.Feature` arrived** — `Feature` and `Split`, moved from VL.Mapsui with their
tests, plus the `NetTopologySuite.Features 2.1.0` dependency. The reasoning and the field-wide
evidence are in [ARCHITECTURE.md](ARCHITECTURE.md#where-a-feature-lives). Seen in the GUI on
2026-08-23 through VL.Overworld's Tutorial 08, and on 2026-09-24 in `HowTo Attach attributes to a
geometry`.

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

## What the name obliges — coverage, measured 2026-10-18

The maintainer asked how much of NetTopologySuite the 39 nodes cover, because a package that takes
the library's name owes its users the library's real capabilities. Measured by reflection over
NetTopologySuite 2.6.0 (`deps\NetTopologySuite.2.6.0`):

| denominator | size | ours | share |
|---|---|---|---|
| public types / public methods, whole assembly | 538 / 2,318 in 66 namespaces | ~30 members called | ~1% — and meaningless: more than half is machinery (`GeometriesGraph` 182 methods, `Mathematics` 106, `Noding` 77, `EdgeGraph`, `Planargraph`, `RTools_NTS`) no patch would ever touch |
| the `Geometry` class — the surface a user meets first | 40 methods + 24 properties = 64 | 20 (Buffer, Contains, Difference, Distance, Intersection, Intersects, Union, Within, GetGeometryN, ToText; Area, Centroid, Coordinates, EnvelopeInternal, GeometryType, IsEmpty, IsValid, Length, NumGeometries, SRID) | **31%** |
| user-facing capability areas (the namespaces a patch author would look for, grouped) | 24 | 10 touched, 4 complete | **42% touched** |

The 24 areas and their state:

| area | state |
|---|---|
| geometry creation (Point … GeometryCollection) | ✅ complete |
| geometry inspection | ◐ most, since tier 1 (2026-11-07): `Envelope`, `InteriorPoint`, `Boundary`, `IsSimple`, `NumPoints` |
| predicates (`Operation.Predicate` / `Relate`) | ✅ 8 named of 9 plus `Relate`, since tier 1; `Disjoint` deliberately absent |
| overlay (`Operation.Overlay` / `OverlayNG`) | ✅ 4 of 4, since tier 1 |
| distance, nearest points | ✅ |
| buffer | ◐ the basic form; no end-cap / join parameters |
| validity (`Operation.Valid`) | ◐ verdict, reason, location; no repair |
| WKT (`IO`) | ✅ |
| spatial index (`Index.*`) | ◐ STRtree of four |
| features (separate package) | ✅ |
| convex / concave hull, minimum bounding circle (`Algorithm`, `Algorithm.Hull`, `Algorithm.Construct`) | ◐ `ConvexHull` since tier 1; concave hull and minimum bounding circle are tier 2 |
| affine transformation (`Geometries.Utilities`) | ❌ |
| simplification (`Simplify`) | ❌ |
| WKB (`IO`) | ❌ |
| GML / KML (`IO.GML2`, `IO.KML`) | ❌ |
| linear referencing (`LinearReferencing`) | ❌ |
| union of many (`Operation.Union`) | ❌ |
| precision (`Precision`) | ❌ |
| polygonize / line merge / dissolve | ❌ |
| triangulation (`Triangulate.*`) | ❌ |
| densify (`Densify`) | ❌ |
| coverage (`Coverage`) | ❌ |
| shape generators (`Shape.*`) | ❌ |
| prepared geometry (`Geometries.Prepared`) | ❌ |

**What the name means in this community:** `VL.OpenCV`, `VL.Skia`, `VL.Mapsui` are each a way *into*
their library, not the whole of it, and ARCHITECTURE's "what stays raw" still holds — the node
browser is not an API dump. But a user who installs a geometry package carries a mental list of what
a geometry library does, and that list comes from Shapely and PostGIS, not from NTS's namespace
tree. Against that list the gaps are basic, not marginal: six predicates, the fourth overlay, union
of many, convex hull, simplify, WKB, translate/rotate/scale, a point at a fraction along a line,
repairing an invalid geometry. So the plan below is ordered by *what a geometry user expects*, not by
NTS's directory.

### Tier 1 — the rest of `Geometry`'s own surface, one line each (15 nodes) — **done 2026-11-07**

`Touches`, `Crosses`, `Overlaps`, `Covers`, `CoveredBy`, `SymmetricDifference`, `ConvexHull`,
`Envelope` (as a geometry), `InteriorPoint`, `IsSimple`, `IsWithinDistance`, `Reverse`, `Relate`
(the DE-9IM string), `Boundary`, `NumPoints`. Takes the `Geometry` share from 31% to about 55%.
`Disjoint` stays out: it is `Intersects` + `Not`, and the rule in "Adding a node" holds.

### Tier 2 — the common operations that live in their own namespaces (~14 nodes)

`Union (Spread)` (`UnaryUnionOp` / `CascadedPolygonUnion`), `Simplify` (Douglas-Peucker) and
`Simplify (Topology Preserving)`, `Read WKB` / `Write WKB` (with the hex form PostGIS uses),
`Translate` / `Rotate` / `Scale` (`AffineTransformation` — the most vvvv-shaped thing NTS has),
`Interpolate` / `Project` / `Substring` (`LinearReferencing`: a point at a fraction along a line,
the fraction of the nearest point, a piece of a line — animation along paths), `Densify`,
`MakeValid` (`GeometryFixer`), `MinimumBoundingCircle`, `ConcaveHull`, `RandomPoints` (in a polygon).
Fills 8 of the 14 empty areas.

### Tier 3 — stateful or advanced

`PreparedGeometry` (a process node, on `SpatialIndex`'s template), `Quadtree` / `KdTree`,
`Polygonize`, `LineMerge`, `Delaunay` / `Voronoi`, `Coverage`, `GeometryPrecisionReducer`,
3D distance, Hausdorff distance. Each when something asks, as the rule at the top says.

**Never:** the machinery — `Noding`, `GeometriesGraph`, `EdgeGraph`, `Planargraph`, `Mathematics`,
`RTools_NTS.Util`. See "Never" below.

Tiers 1 and 2 are about 30 nodes, most of them one call plus the coordinate copy plus a help patch.
One tier per commit, every node with tests and an F1 flag, and the help patches extended rather than
multiplied where a node joins an existing topic (the predicates belong in `HowTo Test how geometries
relate`, the fourth overlay in `HowTo Combine two geometries`).

---

## Next

In order, and each small. **The coverage tiers above come first from 2026-10-18 on**; the
rows below that they subsume say so.

**Done 2026-08-14: the help-patch layouts.** All four were re-laid-out and each checked in the GUI.
The rule that fixed them: **a Pad's `Comment` renders as a label to the right of the box, so an
IOBox occupies far more width than its `Bounds` says** — every label collision came from ignoring
that. Every Pad now has its own row or ~200px of clear space to its right.

From here **the checked-in `.vl` is the source of truth and must not be regenerated**, because that
would discard the layout. Edit `Bounds` in place, anchored on a match asserted to occur exactly
once, and re-run the overlap check.

| | |
|---|---|
| **Release `0.0.1-alpha` — done 2026-09-26** | Browser upload by the maintainer; installed back from nuget.org, 15 help patches compile from it, one run in vvvv. Working version `0.0.2-alpha`. Next release: Trusted Publishing from GitHub Actions, written once for the family ([RELEASE.md](RELEASE.md)). |
| **`Explanation Overview of available nodes.vl` — done 2026-09-24, seen in the GUI** | One per library, the front door — 57 of vvvv's own packs have one. Help is the teaching surface: VL.Skia ships 4 C# nodes and 98 help patches, and in libraries people learn from help runs 16–24% of node count. Four patches against 32 nodes is 12%, so this is under-served rather than done. |
| **The remaining help patches — done 2026-09-24: ten `HowTo`s, one per node group, every category covered, all seen in the GUI** | `HowTo Create a linestring`, `HowTo Inspect a geometry`, `HowTo Intersect two geometries`, `HowTo Test how geometries relate`. Append each to the right `Topic` in `Help.xml` — **do not number the files.** They were numbered `01 03 04 06` at first, and because only four of the eight existed, every gap read as a broken install. `Help.xml` is the only place ordering lives. |
| **The cross-package example** | `Coordinates → Polygon → Buffer → Geometry → VL.Mapsui Feature → Map`, living **outside** both repositories. A patch needing two packages cannot ship inside one whose dependencies do not guarantee the other. Precedent: `vvvv-gis\examples\Example Map with data on it.vl`. |
| **Rebuild detection in `SpatialIndex` / `Network`: reference, then value** | Measured 2026-09-24 in the shortest-path help patch: `Read WKT` makes new objects every frame, so `Read WKT → Geometries → Network` rebuilt every frame (1819 in thirty seconds) while `Read WKT ×6 → Cons → Network` stayed at 1 only because VL's collection builder keeps `Equals` items and NTS compares geometry by value. The reference-only contract in ARCHITECTURE is therefore already value-equality whenever `Cons` is upstream, and a trap whenever it is not. Candidate: keep the reference check first (free), and when references differ fall back to element-wise `EqualsExact` before rebuilding — always cheaper than the rebuild it avoids, and it makes the help patch's `Cache` region a performance choice instead of a requirement. Not done: it changes a documented contract and its 16 + 21 tests, and the user has not decided. |
| **The remaining predicates** — tier 1, **done 2026-11-07** | `Touches`, `Crosses`, `Overlaps`, `Covers`, `CoveredBy`. One line each, left out only to keep the first surface small. `Disjoint` stays out permanently — it is `Intersects` plus `Not`. |
| **`SymmetricDifference`** — tier 1, **done 2026-11-07** | The fourth overlay operation, omitted because it is the least used of the four. |

---

## Later

Ordered by how likely something is to need them. Since 2026-10-18 the coverage tiers above give the
order; these rows keep the reasoning for each item.

| | |
|---|---|
| **WKB** | `Read WKB` / `Write WKB`, plus the hex form PostGIS uses. Genuinely in scope — it is NetTopologySuite's own IO, same as WKT — and deferred only until the core is stable. |
| **`ConvexHull`, `Simplify`** | Douglas-Peucker and topology-preserving simplification. Both common enough to expect; both waiting on a use case that says which tolerance semantics matter. |
| **M ordinates** | `CoordinateM`, `CoordinateZM`, and telling `Write WKT` to emit them. **Measured working** — M survives the default factory intact — so this is a scope decision, not a limitation. XY is stable first, then XYZ, then M. |
| **Validation detail** | `IsValid` already reports a reason and a location. NTS also offers `IsValidOp` with self-touching-ring tolerance, and `GeometryFixer` for repairing invalid geometry. `GeometryFixer` in particular would be useful and needs thought about whether silently repairing geometry is a thing a node should do. |
| **Precision tools** | Fixed-precision models, `GeometryPrecisionReducer`. `GeometryFactory` is already the way in — a `PrecisionModel` built through raw .NET nodes plugs straight into the same pin. A dedicated node waits for someone hitting a robustness problem. |
| **Prepared geometry** | `PreparedGeometryFactory` makes repeated predicates against one fixed geometry much faster, which is exactly the shape of a VL patch testing many points against one polygon every frame. **This one needs `[ProcessNode]`**, not a static method: it holds a prepared index that must be built once and rebuilt only when the geometry changes. Written as a static method it would rebuild the index sixty times a second, which is the same class of mistake as rule 8 in [RULES.md](RULES.md). |
| **Spatial indexing — `Quadtree`** | `STRtree` arrived 2026-08-23 as `NetTopologySuite.Index` (see Now). `Quadtree` deliberately did not: it exists for incremental insertion into a mutable index, which is a different lifecycle from "build once, query many", and no chapter, prompt or user has asked for it. Not to be added because it is on the same NTS page. |
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
| **A `Feature` model of our own** | No `VLFeature`, ever. The cross-package need appeared and was answered on 2026-08-22 by wrapping `NetTopologySuite.Features.Feature` — the NTS team's neutral model — as `NetTopologySuite.Feature`. Wrapping theirs is what keeps us from inventing ours. |
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
