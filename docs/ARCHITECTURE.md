# Why VL.NetTopologySuite is shaped this way

[docs/RULES.md](RULES.md) covers mechanics — what silently breaks when packaging, and what breaks
outside the package at runtime. [docs/AUDIT.md](AUDIT.md) is the audit this package was designed
from, and holds every measurement quoted below. This document is the reasoning: what the package is
for, what it will never contain, and how to decide whether a new node belongs.

It exists because these questions were settled once, deliberately, and re-deriving them costs more
than reading them.

---

## Contents

- [One job](#one-job)
- [Native types, and the one thing we add](#native-types-and-the-one-thing-we-add)
- [The factory, and why SRID is 0](#the-factory-and-why-srid-is-0)
- [Node categories](#node-categories)
- [Decisions, per node](#decisions-per-node)
- [Where a feature lives](#where-a-feature-lives)
- [What stays raw](#what-stays-raw)
- [The package boundary](#the-package-boundary)
- [Relationship with the sibling repositories](#relationship-with-the-sibling-repositories)
- [What this will never contain](#what-this-will-never-contain)

---

## One job

**How does a VL patch create, inspect, and perform basic spatial operations on NetTopologySuite
geometry?**

That is the whole question. Not how to draw it, not how to reproject it, not how to read a
shapefile. Since 2026-08-22 "geometry" includes the **feature** — geometry plus attributes —
because a feature is made *of* geometry and has to be constructible wherever geometry is; the full
argument is in [Where a feature lives](#where-a-feature-lives). The test for any other proposed
addition is in [The package boundary](#the-package-boundary).

**Almost all the value is upstream, and saying so plainly is useful.** NetTopologySuite is two
decades of production use. What this repository solves is *how it shows up as nodes in a patch* —
which turned out to be genuinely hard, and which the sibling repositories learned by shipping nine
releases that installed cleanly and contributed nothing.

The consequence worth stating: **when a node is our own arithmetic rather than NTS's, that is
information the user wants**, because our code lacks NTS's mileage. Every node's doc comment names
where its answer comes from, and vvvv shows it as the tooltip. Almost every node here says "Uses
NetTopologySuite", which is the honest answer — and the defensive copying described below is the
one place where it is not the whole answer.

---

## Native types, and the one thing we add

**No `VLGeometry`, no `VLPoint`, no `VLFeature`.** Nodes take and return
`NetTopologySuite.Geometries.*` directly. Three payoffs, and the third is the real one:

1. Operations chain with no conversion — `Geometry → Buffer → Intersection → Geometry`.
2. Advanced users can reach the rest of NTS through VL's raw .NET nodes and plug the result back in.
3. **A geometry made here crosses into another package with no adapter at all.** VL.Mapsui consumes
   `NetTopologySuite.Geometries.Geometry`; nothing has to know about this package for that to work.

So the public surface stays compatible with ordinary .NET NTS code, and this package improves
usability without hiding the library.

### The exception: defensive copying

There is exactly one behaviour here that is not raw NTS, and it is the reason these are hand-written
nodes rather than reflection nodes over `GeometryFactory`.

**NTS geometries are not deeply immutable.** Measured against 2.6.0:

| | |
|---|---|
| `Coordinate.X` has a public setter | yes |
| `factory.CreatePoint(c)` then `c.X = 777` | the point moves — the factory kept the caller's object |
| `geometry.Coordinates[0].X = 555` | the geometry moves — that property hands out live references |
| `point.Coordinate` | live, same reason |

The default `CoordinateArraySequenceFactory` **wraps** the caller's array rather than copying it.

This contradicts what both sibling repositories assert in prose — `vvvv-gis/docs/DESIGN.md` says
"Immutability, because NetTopologySuite is immutable". The half that is true is that *operations*
return new geometries. The half that is false is the storage.

**Why it matters more in VL than in C#.** A value flowing into two branches of a patch is *the same
reference*, and there is no `readonly` to stop the second branch writing through it. A patch that
holds a `Coordinate`, builds a `Point` from it, hands that Point to VL.Mapsui, and later edits the
`Coordinate` would silently move geometry that is already on screen — with nothing anywhere to say
why.

So: **every creation node copies its coordinates on the way in; every reader node copies on the way
out.** `Defaults.CopyOf` is the whole implementation. `MutationTests` is the regression suite, and it
is negative-tested — removing the copying turns exactly four tests red, which is what makes them
checks rather than decoration.

The cost is one array allocation per geometry built. That is the right trade: the alternative is a
class of bug that is invisible, intermittent, and appears in someone else's package.

---

## The factory, and why SRID is 0

Two things had to be true at once: the common case must not be
`GeometryFactory → CreatePoint`, and SRID and precision must not be hidden, because they affect
correctness.

The answer is an **optional trailing `Factory` pin** on every creation node, plus a
`GeometryFactory` node that produces one from an SRID.

- Unconnected → a shared default: **SRID 0, floating precision**.
- Connected → whatever the patch says, including a factory built through VL's raw .NET nodes for
  fixed precision or a custom coordinate sequence.

**Unconnected is the only thing that can mean "the default".** That is rule 8 of
[RULES.md](RULES.md), learned in the sibling repository at the cost of 444 files written next to two
repositories while every guard reported success. A `GeometryFactory` pin has no "empty" state to
confuse with `null`, so here it is unambiguous — but the principle is why there is no `SRID` pin on
every creation node as an alternative.

### Why 0 and not 4326

`GeometryFactory.Default.SRID` is 0 (measured). 0 means **unset**, and for a package named after
the library that is the truthful default: NTS geometry is planar, not inherently geographic.

Stamping 4326 on coordinates the package has never seen would be a claim it cannot check. And the
claim would be dangerous rather than merely wrong, because **mixing SRIDs never throws**: a 4326
geometry intersected with an SRID 0 one returns 4326 without complaint. A wrong CRS therefore
produces a confident wrong answer that looks exactly like a right one.

**An SRID is a label, not a transformation.** No node here implies that setting it reprojects
anything, because it does not. That work belongs in a focused package — `VL.ProjNet` — and is out
of scope here.

One wart this package does correct: NTS's `WKTReader` stamps **-1** rather than 0 for "unknown",
regardless of the factory it was built from, which would give a patch two different numbers both
meaning "nobody said". `Read WKT` routes the factory's SRID through `NtsGeometryServices` so typed
and built geometry agree. The obvious fixes — the `WKTReader(GeometryFactory)` constructor and the
`DefaultSRID` property — are both `[Obsolete]` in 2.6, and NTS warns that setting `DefaultSRID` may
stop working; the services route is the one it asks for.

### Caching, because a node runs every frame

A `public static` method is evaluated **sixty times a second from the moment the document opens**.
So `new GeometryFactory(new PrecisionModel(), srid)` inside the `GeometryFactory` node would
allocate forever. It is cached per SRID, which also makes the output **reference-stable** — and that
is what keeps the WKT reader cache (keyed on the factory, weakly) from rebuilding a parser and an
`NtsGeometryServices` every frame.

Neither cache needs `[ProcessNode]`: a factory and a reader hold no connection, handle or thread.
This is the cheap end of that rule, not an exception to it.

The other end of the rule arrived on 2026-08-23: **`SpatialIndex` is this package's first
`[ProcessNode]`**, because an index over a hundred thousand geometries is expensive to build and
must survive from one frame to the next. Its lifecycle contract is its own section, below.

---

## Node categories

```text
category = (.NET namespace minus the "VL" prefix) + type name
```

The prefix comes from `[assembly: ImportAsIs(Namespace = "VL")]`.

| Category | Source | How |
|---|---|---|
| `NTS.Geometry` | `GeometryNodes.Creation.cs` + `.Inspection.cs` | namespace `VL.NTS` + `[Name("Geometry")]` |
| `NTS.Feature` | `FeatureNodes.cs` | `[Name("Feature")]` |
| `NTS.Operation` | `OperationNodes.cs` | `[Name("Operation")]` |
| `NTS.IO` | `IONodes.cs` | `[Name("IO")]` |
| `NTS.Index` | `IndexNodes.cs` | `[ProcessNode(Name = "SpatialIndex", Category = "NTS.Index")]` on the class, `[Name("Index")]` on the static `Query` holder |
| `NTS.Experimental.Network` | `NetworkNodes.cs` | namespace `VL.NTS.Experimental` + `[Name("Network")]`; **experimental by name, on purpose** — see its own section |

Five, deliberately — `NTS.Validation` was considered and dropped, because one validity node does
not earn a category level and §26 of the brief warns against deep nesting. `NTS.Feature` earns one
because it wraps a distinct upstream package and a distinct layer of the data model, not merely a
pair of methods. `NTS.Index` (2026-08-23) earns one with two nodes, because the criterion was never
the count: it is whether the category protects a distinct concept — *build an acceleration
structure, then ask it* — that is likely to grow coherently. See its own section below.

**Assembly `VL.NetTopologySuite`, root namespace `VL.NTS`.** The package is named after the library
it wraps, which is the rule for a single-library package; only the *category* is abbreviated,
because a patch author has to read it in a node browser and `NetTopologySuite.Geometry.Point` is
unreadable. Both sibling repositories decouple these two names the same way.

`GeometryNodes` is one `partial` class across two files rather than two classes, so both halves land
in one category without depending on two types being allowed to share one.

Two things that were established rather than guessed, by reading `VL.Core`'s metadata:

- **`NameAttribute`'s `AttributeUsage` is `All`**, so `[Name("Read WKT")]` is legal on a *method*,
  not only a type. That is how the node is called `Read WKT` rather than whatever VL makes of
  `ReadWKT`. **Whether VL's importer honours it on a member has not been shown** — only the GUI can.
- **`PinAttribute` targets parameters and return values**, with a `Name` property. That is how the
  `wkt` parameter becomes a pin labelled `WKT`.

Neither sibling repository uses either on a member, so both are new ground here and both are listed
as unverified in the README.

---

## Decisions, per node

The eleven one-line wrappers all have the same answers: no state, no mutation, native output, and
they exist because a constructor or an instance method is not reachable as a node. The ones with a
real decision behind them:

### `Coordinate` / `CoordinateZ` — two nodes, not one with an optional Z

`Coordinate` and `CoordinateZ` are two distinct NTS types, so this is not the duplicate-convenience
family the brief's §39 forbids — it is the type distinction, surfaced. An optional `Z` pin would
have to be a nullable float, which reads worse and would still have to pick a type internally.

`CoordinateM` and `CoordinateZM` are left out of v1 as a scope decision, not a limitation: M
ordinates were measured surviving the default factory intact, so support is a node away when
something needs it.

### `Split (Coordinate)` — because an opaque value is undebuggable

A `Coordinate` on a pin is a name with no way to see inside. **Every opaque value needs a reader
node** — the rule the sibling repository arrived at across four instances. `Split` is the name the
Gray Book gives this, and the one a user types: it appears 194 times in shipped help patches.

### `Polygon` takes rings, not coordinates

The brief's §35 sketches `Coordinates → Polygon`. This package makes it
`Coordinates → LinearRing → Polygon`, one node longer, and that is deliberate:

- **Composition over giant nodes.** `LinearRing` is a real NTS type and a real concept; hiding it
  inside `Polygon` would mean the patch author learns about rings only when a hole goes wrong.
- **`Shell` and `Holes` are the same type**, so the pins explain each other.
- The alternative was a second `Polygon` overload taking coordinates, which is exactly the
  duplicate-convenience family §39 rules out.

The convenience is not lost, it is *named*: `LinearRing` carries the auto-closing.

### `LinearRing` closes the ring, and says so

NTS throws `points must form a closed linestring` on an open ring (measured). Four corners of a
rectangle — not five — is what a person patches. So the ring closes itself, with a **copy** of the
first coordinate rather than a second reference to it, and the doc comment leads with the fact.

Deliberately *not* silent about the limit of that convenience: **closure is checked at construction,
validity is not.** A ring that crosses itself closes perfectly and builds an invalid polygon.
`IsValid` is what catches it, and the doc comment on `LinearRing` points there.

`Read WKT` closes rings too, via `FixStructure`, so the two routes into a polygon behave the same.

### `IsValid` has three outputs

`IsValidOp` hands over the reason and the coordinate for free — `Self-intersection` at `(1, 1)`,
measured — and a bare `false` is the least useful thing a validity node could say. Extra *outputs*
cost a patch nothing; the three-input guideline is about inputs.

`Geometry.IsValid` computes the same thing and throws the reason away, which is why this uses
`IsValidOp` directly.

### `Bounds` returns four floats, not an `Envelope`

**Return primitives when a type has no representation in a patch.** NTS's `Envelope` cannot be
opened by any node, so returning one would leave the caller with neither the numbers nor a way to
get them. NTS's `Geometry.Envelope` is a third thing again — a rectangular *Polygon* — and is
reachable raw if that is what someone wants.

### `Read WKT` reports failure on a pin

Half-typed text is the **normal** state of an IOBox someone is editing, and a node that throws sixty
times a second is not a diagnostic. `Success` goes false and the geometry goes empty.

It catches broadly on purpose: NTS throws `ParseException` for malformed text and `ArgumentException`
for text that parses and then describes something illegal (measured). From the patch author's side
those are one problem.

### `Write WKT` has an `Include Z` pin and no SRID pin

NTS's writer **drops Z silently** at its default dimension of 2 — a Point built from a
`CoordinateZ` writes as `POINT (1 2)`, elevation gone (measured). That is a real way to lose data on
the way out of a patch, so it is a pin rather than a default nobody looks at.

There is no SRID pin because `WKTWriter` in 2.6 has no way to emit one — checked against the type's
own members. Plain WKT has nowhere to put an SRID, and the node does not pretend otherwise.

### `Buffer` defaults to 8 segments

8 is NetTopologySuite's own default. Raising it to 16 would give prettier circles and would be a
silent divergence from the library this package is named after; the pin is visible and the doc
comment says which way to move it. Same reasoning as the SRID: match NTS unless there is a
correctness reason not to.

### `Disjoint` does not exist

It is `Intersects` with a `Not` after it. **A node a patch can already build from two nodes is a
help patch, not a node.** `Within` *does* exist despite being `Contains` with the inputs swapped,
because swapping two links to change the question is easy to misread later, and both names are what
people search for.

---

## `SpatialIndex` / `Query` — the first process node

Added 2026-08-23 for VL.Overworld's Tutorial 11, whose lesson is *a candidate is not a result*.
Two nodes in a new category, `NTS.Index`, and the first stateful object in the package. Every
decision below was made before the code, and each is pinned by a test in `IndexTests`.

### Why a process node, and why a new category

An index over 100,000 geometries takes real time to build. A static method would build it sixty
times a second from the moment the document opened — rule 8 in [RULES.md](RULES.md), the rule that
once opened 17,000 TCP connections. So the tree is held, and `Indexes Built` is exposed as a pin:
it should reach 1 and stay, and if it climbs every frame the patch is re-creating its geometries
every frame and the index is doing nothing.

`NTS.Validation` was refused a category for holding one node; `NTS.Index` gets one for holding two.
The count was never the criterion. The criterion is whether the category protects a concept that
is distinct from its neighbours and likely to grow coherently. Spatial indexing is neither making
geometry nor operating on it — its model is *build an acceleration structure, then ask it* — and if
it grows it will grow with nearest-neighbour and prepared-geometry queries, which belong together.
Nothing is added now to fill it.

### The lifecycle contract: rebuild when the set of geometry REFERENCES changes

This is the decision that had to be investigated rather than assumed. The evidence:

- **VL's only change signal is object identity.** `Spread<T>` has no `Equals` override (measured:
  two spreads over `{1,2,3}` compare `False`); `Cache`, `Changed` and `ChannelFlange` all reduce to
  `ReferenceEquals`; no VL data type carries a version or dirty flag. A static node upstream
  therefore hands out a **fresh spread every frame** even when every geometry inside is the same
  object.
- **VL.Mapsui's `FeatureLayer` — the family's only precedent — learned this on screen**: comparing
  its features by reference rebuilt the layer every frame and the map flickered. It now compares
  element-wise, `ReferenceEquals` first and `EqualsExact` (every coordinate) as the fallback.
- **This package already forbids the mutation that would make identity lie.** Every creation node
  copies coordinates on the way in, every reader copies on the way out (`MutationTests`, negative-
  tested). A geometry made here cannot be moved through the handles the patch holds.

So: the collection reference is **not** what is compared — that would rebuild every frame under a
static producer. The coordinates are **not** what is compared either — `EqualsExact` over 100,000
geometries per frame costs more than the index saves, and the user's brief named that trap. What is
compared is the **elements, by reference**: same count, and the same object at every position. A
hundred thousand pointer comparisons is tens of microseconds. It also catches the case identity
alone would miss — the same mutable `List` with an item added — which is a test.

The stated contract, in the node's remarks and in a test whose name says it: **mutating a geometry
already in the index is unsupported and undetectable.** It is reachable only with a raw NTS handle
obtained outside this package. To change geometry, hand in a new collection; the node rebuilds once.

### Smaller decisions

- **`Build()` is explicit.** NTS builds an STRtree lazily on the first query. The node calls
  `Build()` itself after the last insert, so the cost lands where the patch can see it and
  `Indexes Built` means exactly what it says. NTS allows `Build()` once per tree; a rebuild is a new
  tree, so that constraint costs nothing. The test: an `Insert` into the returned tree throws.
- **`Query` takes a geometry, not four floats.** `Bounds` returns four floats because *reading* an
  extent is a debugging act; *searching* by one is a spatial act, and the patch already has the
  geometry — a buffered point, a rectangle, a polygon. Its `EnvelopeInternal` is used inside. This
  keeps `Envelope` off the public surface, which the `Bounds` decision above already chose.
- **The output is named `Candidates`.** NTS documents `Query` as *"items whose bounds intersect the
  given envelope"*. A diagonal line is a candidate for a corner it passes nowhere near
  (`A_candidate_is_not_a_result`). The exact predicate is the patch's job, and the name says so.
- **The handle is the raw `STRtree<Geometry>`.** Native types, and the one thing we add. Nearest-
  neighbour, removal and node capacity stay reachable through raw .NET nodes and are not wrapped
  until a chapter needs them.
- **STRtree only.** `Quadtree` exists for incremental insertion into a mutable index — a different
  lifecycle, waiting for a use case (ROADMAP).
- **Nulls and empty geometries are skipped, not counted, not indexed**; an empty geometry has no
  envelope. A null collection yields no index, so `Query` can be wired before there is data.

---

## The experimental network — and why it is `NTS.Experimental.Network`, not `NTS.Network`

Added 2026-08-23 for VL.Overworld's Tutorial 13, *close does not mean reachable*. Two nodes,
`BuildNetwork` (a process node) and `ShortestPath` (stateless), and one opaque handle type,
`Network`. Every line of it is deliberately provisional, and the category name says so.

### The distinction that sets the scope bar

`SpatialIndex` and this are not the same kind of change. For STRtree, **NetTopologySuite owns the
algorithm and the data structure**; the work was a correct VL lifecycle around an existing NTS
capability. For shortest paths, NTS ships **nothing** — the 2.6.0 API documentation contains the
word "shortest" zero times, and `Planargraph.PlanarGraph` describes itself as a framework that
*"must be subclassed to expose appropriate methods"*. So adjacency construction and Dijkstra here
are **an algorithm of ours**. Publishing an algorithm of ours under the name of a library that does
not contain it is a scope decision, and a chapter is not enough evidence to make it.

Hence: the chapter is approved, a permanent public `NTS.Network` is not. The code lives here because
the alternatives are worse — a new package created to resolve uncertainty about where code belongs
turns the uncertainty into a dependency, and a Dijkstra written in dataflow inside the chapter would
bury the lesson under its own implementation. It lives under `Experimental` so nobody mistakes it for
a promise. It is promoted, moved, or removed **after** three things exist: the chapter, the
abstraction that actually emerged from building it, and at least two more genuine consumers wanting
the same model (a building–entrance–street connectivity prompt, a procedural network prompt, an
accessibility experiment are the plausible ones). That review is a separate Network Package Scope
Proposal, not a line in this file. The evidence it will need — chapter, emerged abstraction, the API
that now looks useful, two consumers in outline — is in [NETWORK-SCOPE-EVIDENCE.md](NETWORK-SCOPE-EVIDENCE.md).

### The scope, as one sentence, asserted by 18 tests

> An undirected spatial network built from **explicitly** connected LineStrings in a local
> Cartesian space, with geometric length as cost and Dijkstra as the path algorithm.

If a change would make that sentence false, the change waits for the review above.

### Decisions

- **Connectivity is exact shared endpoints.** No tolerance, no automatic noding. Two lines that
  cross in XY are *not* connected — the bridge over the river and the overpass over the road are
  the chapter's own subject, and geometry alone cannot tell a crossing from a junction. A line
  passing exactly through another line's endpoint does not connect either: **only a LineString's
  first and last coordinates are nodes; interior vertices are shape.** Where a junction is meant,
  the linework is noded first (`Union` does it), and the chapter presents that step as a *claim
  about the world*, not a geometric fact. Proximity ≠ connectivity is the lesson; a tolerance would
  blur it and auto-noding would hide it.
- **From / To are Points**, not arbitrary geometry: the meaning is "from this location to that one".
- **Snapping is to the nearest node, and visible.** `From Snap Distance` / `To Snap Distance` are
  pins; on a marked place they read 0, off the network they say the route did not start where you
  clicked. No snap tolerance, no silent rejection. Nearest-point-on-edge with edge splitting is
  non-scope.
- **Cost is `LineString.Length`.** No cost pin, no speeds, no callback.
- **Undirected.** One-way streets are non-scope.
- **`ShortestPath` is stateless.** Dijkstra over a hand-typed town is microseconds; a `Paths
  Computed` counter was designed and then dropped because it would teach that path queries are
  something to retain, which is false here. The retained thing is the topology — `BuildNetwork`
  holds the graph and exposes `Networks Built`.
- **Same rebuild contract as `SpatialIndex`, literally shared code** (`InputSets`): rebuild when the
  set of LineString references changes. Closing a bridge is removing a line from the collection —
  a new collection, one rebuild, the counter ticks. No `Enable Edge` channel to dodge the rebuild;
  *new topology → new network* is the clearer contract. Mutating a line in place is unsupported and
  undetectable, and a test says so by name.
- **The path keeps original edge geometry**, each edge reversed where the route walks it against
  the direction it was typed, junction coordinates deduplicated. The graph decides which edges; the
  geometry decides what the path looks like.
- **`Found = false` is a result.** Empty path, length 0, no exception, no silent straight line.
- **Adjacency list + `PriorityQueue`**, not a `PlanarGraph` subclass: the framework offers nothing
  for Dijkstra and would add an inheritance layer to code that is meant to stay small enough to
  delete.
- **The chapter's coordinate space is local Cartesian, one unit = one metre, and says so.** Not
  WGS84 — chapter 10 spent itself on why a length in degrees is not a distance, and chapter 13
  demonstrates the positive form: define the space as metres and `Length` legitimately *is* metres.

---

## Where a feature lives

**Decided 2026-08-22, after researching the question rather than arguing it**: the `Feature` and
`Split` nodes moved here from VL.Mapsui, into a new `NTS.Feature` category, and this package gained
its second — and only other — upstream dependency, `NetTopologySuite.Features 2.1.0`.

### The question

A feature — geometry plus attributes — is produced by hand-construction, by GeoJSON parsing, and by
map picking, and consumed by GeoJSON writing, map drawing and attribute lookup. Multiple producers,
multiple consumers: which package owns the constructor? Until this date it was VL.Mapsui, which
meant **a feature could not exist without a map engine installed** — absurd for a data object, and
it blocked any lesson that builds a dataset without drawing a map.

### The evidence

Every standard defines the feature as a data-model object, never a rendering one — ISO 19109 calls
it an abstraction of a real-world phenomenon whose geometry is one attribute among others; RFC 7946
spells it geometry + properties (+ id) in an interchange format. And every geometry core in the
field deliberately excludes it, placing it one layer up:

| ecosystem | geometry core (no feature type) | the feature type lives in |
|---|---|---|
| Java | JTS — "pure shapes with no meaning" (GeoTools FAQ) | GeoTools `org.geotools.feature` |
| Python | Shapely/GEOS | `fiona.model.Feature` (IO), GeoPandas rows |
| .NET | NetTopologySuite | **`NetTopologySuite.Features`** — the NTS team's own companion, depending only on the core; every NTS IO package depends on it |
| C/C++ | — | `OGRFeature`, in OGR's data-access layer |
| QGIS | (GEOS inside) | `QgsFeature` in `qgis_core`, consumed by separate renderers |

The renderer side is just as uniform, and Mapsui itself is the proof: `Mapsui.dll` has **zero
geometry dependencies** and defines its own scene-level feature (`Mapsui.IFeature`,
`PointFeature`); NTS enters through the bridge package `Mapsui.Nts`, whose `GeometryFeature` is
what `NetTopologySuite.Features.Feature` gets **converted into** at the provider boundary — which
is exactly what VL.Mapsui's `FeatureLayer` does with the features these nodes make. A renderer
converts into its own feature; it never owns the data model users author against.

The genuine counterexamples — OGR and Fiona define the feature inside the reader — work only where
the reader is effectively the sole producer. Here, hand-construction is itself a producer.

### What the decision costs, and what it deliberately leaves alone

- The second upstream package. Accepted because it is the **same team's companion to the same
  library**, pinned at the version both sibling repositories already declare — see the boundary
  section below for the revised wording.
- VL.Mapsui keeps a private six-line helper to build features internally; internal plumbing is not
  a node surface.
- VL.GeoJSON's read-side `Split` and `GetProperty` **stay where they are for now** — consolidating
  them here would give VL.GeoJSON a dependency on this package, which changes the family's
  "compose through NTS types, reference nobody" architecture and is a separate decision, not a
  rider on this one.

---

## What stays raw

Reachable through VL's raw .NET nodes, deliberately not wrapped. The test — *is this a common
geospatial concept that benefits from a VL-native node?* — answers no for all of it:

- **Infrastructure**: `PrecisionModel`, `CoordinateSequence`, `CoordinateSequenceFactory`,
  `NtsGeometryServices`. Not hidden, not wrapped, not silently altered.
- **Configuration objects**: `BufferParameters`, `EndCapStyle`, `IsValidOp`. Behind pins where they
  matter. A 4-input `Buffer` node to expose an end-cap enum on a rare path is two decisions wearing
  one node.
- **The long tail of `Geometry`**: `IsSimple`, `Normalized`, `Reverse`, `Boundary`, `InteriorPoint`,
  `PointOnSurface`, `Relate`, `EqualsTopologically`, and roughly fifty more. All present, none
  wrapped until asked for.
- **Whole subsystems**: prepared geometry, `Quadtree`, `LinearReferencing`, `Triangulate`,
  `Precision.*`, most of `Operation.*` and `Algorithm.*`. (`STRtree` left this list on 2026-08-23
  and became `NTS.Index`; its nearest-neighbour, removal and node-capacity APIs stay raw — the
  handle `SpatialIndex` returns *is* the NTS tree, so they are one raw .NET node away.)
- **WKB and GeoJSON.** See [ROADMAP.md](ROADMAP.md).

NetTopologySuite is large. Wrapping all of it would turn the node browser into an API dump, which is
the failure mode the brief's §28 names and which no user benefits from.

---

## The package boundary

Apply this whenever considering an addition:

| If it answers | It belongs in |
|---|---|
| How do I create or manipulate geometry? | **here** |
| How do I attach data to a geometry, or read it back? | **here** — `NTS.Feature`, see [Where a feature lives](#where-a-feature-lives) |
| How do I display this geometry on a map? | `VL.Mapsui` |
| How do I transform between coordinate reference systems? | a focused package — `VL.ProjNet` |
| How do I read this specific geospatial dataset format? | a focused package |
| Nothing yet — we imagine needing it | **nowhere. Do not build it.** |

**One package per wrapped library.** This package wraps NetTopologySuite and nothing else. Its
NuGet dependencies are `NetTopologySuite 2.6.0` and — since 2026-08-22 — the NTS team's own
companion `NetTopologySuite.Features 2.1.0`, and that is the shape to keep: "the library" means the
NTS project, whose feature model ships as a separate package by *their* packaging choice, not a
different library by ours. The moment a genuinely foreign library appears in the csproj, either it
belongs in its own package or this one has stopped being what its name says.

**Declare the upstream nuget, forward only your own assembly.** Every community package that wraps a
third-party library does this. Forwarding NTS's own assembly would make this repository responsible
for how NTS's entire API looks as nodes.

---

## Relationship with the sibling repositories

Two other repositories sit beside this one under `D:\2026_Projects\`.

### `vl-mapsui` (`VL.Mapsui`) — composes through NTS, not through us

```text
Coordinates → LinearRing → Polygon → Buffer → NTS Geometry → Feature [NTS.Feature]
                                                                  │
                                                    ── package boundary ──
                                                                  │
                                              VectorStyle → FeatureLayer → Map
```

**Neither package references the other, and neither should.** They share the native NTS types.
VL.Mapsui consumes `NetTopologySuite.Geometries.Geometry` and `NetTopologySuite.Features.Feature` —
both made here, both crossing the boundary with no adapter; the conversion into Mapsui's own
scene-level feature happens inside VL.Mapsui's `FeatureLayer`, which is the adapter pointing the
right way.

Consequently **no cross-package example patch lives in this repository.** A patch needing two
packages cannot ship inside one whose dependencies do not guarantee the other; there is already a
precedent for where it goes — `vvvv-gis\examples\Example Map with data on it.vl`, which sits outside
both packages it needs.

Both repositories pin **NetTopologySuite 2.6.0**, and the whole 2.x line carries assembly version
`2.0.0.0`, so a vvvv loading both sees one identity. That is worth keeping deliberately: the reason
VL.Mapsui and VL.GIS could not coexist for months was a shared library resolved to two
incompatible versions in vvvv's flat, machine-wide `%LOCALAPPDATA%\vvvv\gamma\nugets\`.

### `vvvv-gis` (`VL.GIS`) — a reference, not a constraint

VL.GIS `0.2.0-alpha` is on nuget.org and already ships roughly 40 nodes over NetTopologySuite under
`GIS.Geometry` and `GIS.Serialization`. The overlap with this package is large and it is
**deliberately not reconciled yet**.

This package is a clean slate. It does not depend on VL.GIS, does not preserve its node names, and
carries no compatibility aliases or migration infrastructure. VL.GIS's structure predates a clear
package-boundary strategy, and inheriting its decisions would defeat the point of starting again.
The question asked of each of its nodes was not "how do we migrate this?" but **"would we
independently design this node this way today?"** — reuse the idea where yes, ignore it where no.

Four things that answer came back "no" on, recorded because they are the substance of the redesign:

1. **`Coordinate` never appears.** VL.GIS uses `(double longitude, double latitude)` tuples
   throughout, so NTS's own `Coordinate` type is invisible, `Z` is reachable only through a separate
   `CreatePoint3D`, and `M` is unreachable. Here `Coordinate` is a first-class value with a reader.
2. **SRID 4326 is hardcoded** into a private factory with no way to reach it — friction removed and
   the semantics taken along with it. Here the default is 0 with the factory exposed.
3. **Naming**: `CreatePoint`, `ParseWkt`, `ToWkt`, `GetCoordinates`. The category already provides
   the context, so the verb does not need to.
4. **Nothing defends against the coordinate mutability** described above.

What VL.GIS gets right and is reused: the `[Name]` category trick, the per-node attribution of which
library the answer comes from, the units caveat on every node that has one, and — most of all — the
packaging rules in [RULES.md](RULES.md), which cost nine releases to learn.

Whether VL.GIS is later refactored onto this package, deprecated, turned into an umbrella, or left
as a legacy experiment is **a separate decision for after this package is stable and proven**. It is
not being pre-solved here.

---

## What this will never contain

Recorded as *never* rather than *later*, so it stops coming up:

- **Rendering, styling, layers, maps.** A different question, and `VL.Mapsui` answers it.
- **CRS transformation.** ProjNet's job. Nothing here will imply that setting an SRID reprojects.
- **A `Feature` model of our own.** No `VLFeature`, ever. The neutral model this package now wraps
  in `NTS.Feature` is `NetTopologySuite.Features.Feature` — the NTS team's, not ours — and wrapping
  it is precisely what keeps this package from becoming the definition of the whole domain.
  Inventing a type here would do the opposite.
- **Generalised GIS abstractions** — `IGISGeometry`, `SpatialEntity`, `GISContext`, `GISDocument`.
  No current problem requires them.
- **A generic geospatial file-format package.** WKT is in scope because it is NTS's own IO. Letting
  every format follow is how a focused package stops being one.
