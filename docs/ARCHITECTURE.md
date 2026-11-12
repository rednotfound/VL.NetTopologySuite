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
- [Nodes in VL's shape, coordinates in the map's](#nodes-in-vls-shape-coordinates-in-the-maps)
- [Native types, and the one thing we add](#native-types-and-the-one-thing-we-add)
- [The factory, and why SRID is 0](#the-factory-and-why-srid-is-0)
- [Node categories](#node-categories)
- [Decisions, per node](#decisions-per-node)
- [Where a feature lives](#where-a-feature-lives)
- [Drawing without a renderer — the HDE companion](#drawing-without-a-renderer--the-hde-companion)
- [GeometryEditor — drawing in a window of the editor](#geometryeditor--drawing-in-a-window-of-the-editor)
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

## Nodes in VL's shape, coordinates in the map's

Decided by the maintainer on 2026-11-08, the day `0.0.5-alpha` was packed. Two observations
started it, and they turned out to be one problem.

> 我们现在的生成 geometry 的方法都是从给坐标开始的，然后很不直观，比如绘制一个 rectangle，我都给出
> 四个点的坐标……一般人都是给中心点或者左上角，然后再给长宽。
>
> 从 wkt 生成 Geometry 的节点居然叫 Read WKT，而人的思维方式是我要从 wkt 创建 geometry，那才是目
> 的呀。

**The diagnosis: the first 56 nodes were named and shaped after NTS's API, not after the way a VL
user thinks.** `WKTReader.Read` became `Read WKT`; `GeometryFactory.CreatePolygon(shell)` became a
node that takes rings of coordinates. Each was a faithful wrapper and each asks the user to think in
NTS's terms. The nodes are correct; the *question they answer* is the library's, not the user's.

**The principle, in two halves:**

1. **The organisation follows VL.** Node names, pin names, pin order and the way a shape is described
   are learned from VL's own libraries, VL.Skia above all, so that a user who has drawn a rectangle in
   Skia already knows how to make one here. The maintainer: *"我们是学习 Skia 的组织结构"*.
2. **The coordinates follow the map.** x grows east, y grows **north**. The maintainer: *"坐标还是
   follow 地图坐标才是我们的核心！我们不太管屏幕坐标！"* This package does not know that a screen
   exists, as it does not know that a map renderer exists. Screen space (y down) is VL.Skia's
   business; the HDE viewers negate y on the way to Skia, and that is the only place a screen is
   allowed to appear.

The two halves meet in one place and must be stated there: **an anchor named after a direction means
that direction on a map.** `TopLeft` is the north-west corner, the one with the smallest x and the
**largest** y, where in Skia it is the smallest y. Same enum, same words, the corner a person points
at on a map.

### What was measured

In the vvvv 7.4 install (help patches of every shipped pack, VL.Core by reflection):

| | |
|---|---|
| How VL describes a shape | **Position, Size, Anchor.** `Rectangle` 277 uses, `Circle` 43 (Position, Radius), `Rectangle (Join)` 36, `Rectangle (Bounds)` 18, `RoundRectangle`, `Segment` (Position, Size, Cycles), `Rectangle (Join Points)`, `CircleSpread` (Center, Count, Width, Phase). Nobody hands VL four corners to get a box |
| The anchor | `VL.Lib.Mathematics.RectangleAnchor`: TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight |
| A position | `Stride.Core.Mathematics.Vector2`. **This package accepts none**: `Vector2` occurs zero times in `src\`, so a mouse position or a Skia point has to be split into X and Y and rejoined into a `Coordinate` before it becomes a point |
| How VL names a conversion | `FromX` / `ToX`: `FromValue` 86, `FromHSL` 21, `FromPolar` 6, `FromBytes` 4, `FromGeographic` 3; `ToString` 41, `ToPath` 3. **`Read` means a file or a stream** (`FileReader`, `ReadBackTexture`) |
| Angles | VL measures them in **cycles**, 0 to 1 a full turn (`Segment`'s Cycles, `FromPolar`) |

Elsewhere: Shapely 2.0 turned `.wkt` into the functions `from_wkt` / `to_wkt`; PostGIS names it
`ST_GeomFromText` / `ST_AsText`; Grasshopper, the closest node-based tool, builds `Rectangle` from a
plane and X/Y sizes and `Polygon` from a radius and a segment count; Turf.js builds grids from a
bounding box and a cell size. Nobody calls it reading.

And NTS already has the arithmetic, unwrapped (2.6.0, by reflection): `GeometricShapeFactory`
(`CreateRectangle`, `CreateCircle`, `CreateEllipse`, `CreateArc`, `CreateArcPolygon`,
`CreateSquircle`, `CreateSupercircle`, with `Centre`, `Base`, `Width`, `Height`, `NumPoints`,
`Rotation`), `SineStarFactory`, `Shape.Random.RandomPointsBuilder`, `Shape.Fractal.*`. So the shape
nodes are NTS's answer behind VL's question, which is exactly the job this package has.

### What it decides

- **Shape nodes, pins after VL.Skia.** `Rectangle` (Position, Size, Anchor; default `Center`),
  `Rectangle (Join Points)` (two corners), `Circle` (Position, Radius, Resolution), `Ellipse`
  (Position, Size, Rotation, Resolution), `RegularPolygon` (Position, Radius, Sides, Rotation),
  `Arc` and `Sector` (Position, Radius, Start, Sweep). Angles in cycles, as in VL; a circle is a
  polygon, so it needs a segment count where Skia does not.
- **Positions are `Coordinate`; `Vector2` comes through two conversion nodes.** Planned first as
  `Vector2` pins on every node, and measured before it was built: `Vector2` is Float32, and a
  Float32 map coordinate is about a metre coarse (a step of 1.5e-5 degrees at Tokyo's longitude,
  1.4 m; a step of 1 m at a Web Mercator easting of 15,550,000). Stride's `Double2` exists and
  appears in no shipped help patch. The maintainer chose `Coordinate` (double) everywhere, with
  `Coordinate (FromVector2)` as the way in from a mouse, Skia or Stride, which loses nothing
  (every Float32 is exactly a double). `ToVector2`, the way back, was built and dropped the same
  day: it was the one node that lost precision silently, and drawing does not need it - `Outlines`
  hands Skia its points. The maintainer agreed: positions are `Coordinate`, full stop. A shape
  takes Width and Height as doubles for the same reason, where Skia takes a `Vector2` Size.
- **`Read WKT` → `FromWKT`, `Write WKT` → `ToWKT`, no alias.** The maintainer: *"Read WKT 可以不留
  了"*. Two names for one node clutter the NodeBrowser, and this package has never carried
  compatibility aliases. It breaks every patch that uses them (the 16 help patches, VL.Overworld,
  VL.Mapsui's help); still alpha, so now is the cheapest moment. VL.NetTopologySuite.GeoJSON should
  follow with `FromGeoJSON` / `ToGeoJSON`, in its own repository, by its own rules.

### The test, for every node from here on

Before NTS's method name: **what does the user want to get, and from what?** The answer is the node
name (`FromWKT`: a geometry from WKT). Before NTS's parameters: **how does VL already describe this?**
The answer is the pins (Position, Size, Anchor). Then **which way is up?** North, always.

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
| `NetTopologySuite.Geometry` | `GeometryNodes.Creation.cs` + `.Inspection.cs` | namespace `VL.NetTopologySuite` + `[Name("Geometry")]` |
| `NetTopologySuite.Feature` | `FeatureNodes.cs` | `[Name("Feature")]` |
| `NetTopologySuite.Operation` | `OperationNodes.cs` | `[Name("Operation")]` |
| `NetTopologySuite.IO` | `IONodes.cs` | `[Name("IO")]` |
| `NetTopologySuite.Index` | `SpatialIndexNode.cs` + `IndexNodes.cs` | `[ProcessNode(Name = "SpatialIndex", Category = "NetTopologySuite.Index")]` on the class, `[Name("Index")]` on the static `Query` holder |
| `NetTopologySuite.Network` | `Network.cs` + `NetworkNode.cs` + `NetworkNodes.cs` | `[ProcessNode(Name = "Network", Category = "NetTopologySuite.Network")]` on the class, `[Name("Network")]` on the static `ShortestPath` holder. Was `NetTopologySuite.Experimental.Network` until the 2026-08-28 promotion — see its own section |

**Source files, one convention (measured 2026-10-17 against VL.CoreLib, VL.ImGui, VL.Skia,
VL.IO.Redis, VL.OpenCV, VL.Devices.Kinect2 and vl-mapsui):** one type per file, named after the
type; a static class of operations is `<Topic>Nodes.cs`, which is CoreLib's own `SpreadNodes.cs`,
`ArrayNodes.cs`, `KeyboardNodes.cs`; a class split across files is `<Class>.<Part>.cs`, CoreLib's
`EnumerableNodes.Custom.cs`. The Gray Book sets no file-naming rule — the namespace and the class
decide the category, the file name decides nothing — so this is the community's habit, kept here
because it is what makes *node → category → file* a two-step lookup. Until 2026-10-17 `IndexNodes.cs`
held two types and `NetworkNodes.cs` three; both were split, with no code change.

Six, deliberately — `NTS.Validation` was considered and dropped, because one validity node does
not earn a category level and §26 of the brief warns against deep nesting. `NetTopologySuite.Feature` earns one
because it wraps a distinct upstream package and a distinct layer of the data model, not merely a
pair of methods. `NetTopologySuite.Index` (2026-08-23) earns one with two nodes, because the criterion was never
the count: it is whether the category protects a distinct concept — *build an acceleration
structure, then ask it* — that is likely to grow coherently. See its own section below.

**Assembly `VL.NetTopologySuite`, root namespace `VL.NetTopologySuite`, top category
`NetTopologySuite`.** Until 2026-10-17 the namespace was `VL.NTS` and the category `NTS`, chosen
because `NetTopologySuite.Geometry.Point` read as too long in a node browser. That reasoning was
wrong, and the maintainer found out the way a user would: they installed the package, searched the
NodeBrowser for "NetTopologySuite" - the name they had just typed into `nuget install` - and found
nothing. Measured the same day over the 55 VL packs installed on this machine: every pack that does
not join an existing category names its top category after the package minus `VL.` (`Mapsui`,
`OpenCV`, `Audio`, `Elementa`, `IO.Redis`), and not one abbreviates - `NTS` was the only one. The
Gray Book's design guideline says the same: *the name of the main document of a library should hint
at the category the library introduces.* There is no other lever: the NodeBrowser searches node
names and categories, `ProcessNodeAttribute.Tags` exists for process nodes only (two of 39), and
static methods have no tag mechanism at all. So the category carries the library's full name, like
every neighbour - VL.Mapsui's namespace is `VL.Mapsui` and its category `Mapsui`, the same shape.
Existing documents were *mostly* not broken by the rename: a `.vl` resolves a node by name within
the referenced package, and its `LastCategoryFullName` is a hint (negative-tested in September with
`NTS.Wrong`). **But a node that carries a hard `<CategoryReference Kind="Category" Name="NTS">`
is enforced by `vvvvc`**, and VL.Overworld's Tutorial 1.1 failed with `Not found: Point` on exactly
that on 2026-10-18 - so a category rename is a breaking change for any consumer whose patches were
written with the category pinned, and the `0.0.2-alpha` release notes' "patches keep working" was
true only of the hint form. This package's own help patches pin no category of ours. One consequence in C#: inside namespace `VL.NetTopologySuite` the bare identifier
`NetTopologySuite` resolves to our own namespace, so the three fully qualified NTS names in code
carry `global::`.

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
get them. NTS's `Geometry.Envelope` is a third thing again — a rectangular *Polygon* — and since tier 1
(2026-11-07) it is the `Envelope` node, for the patch that wants the box as something `Intersects`
or a map layer can take. `Bounds` stays: numbers connect to anything.

### `FromWKT` reports failure on a pin

Half-typed text is the **normal** state of an IOBox someone is editing, and a node that throws sixty
times a second is not a diagnostic. `Success` goes false and the geometry goes empty.

It catches broadly on purpose: NTS throws `ParseException` for malformed text and `ArgumentException`
for text that parses and then describes something illegal (measured). From the patch author's side
those are one problem.

### `ToWKT` has an `Include Z` pin and no SRID pin

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
Two nodes in a new category, `NetTopologySuite.Index`, and the first stateful object in the package. Every
decision below was made before the code, and each is pinned by a test in `IndexTests`.

### Why a process node, and why a new category

An index over 100,000 geometries takes real time to build. A static method would build it sixty
times a second from the moment the document opened — rule 8 in [RULES.md](RULES.md), the rule that
once opened 17,000 TCP connections. So the tree is held, and `Indexes Built` is exposed as a pin:
it should reach 1 and stay, and if it climbs every frame the patch is re-creating its geometries
every frame and the index is doing nothing.

`NTS.Validation` was refused a category for holding one node; `NetTopologySuite.Index` gets one for holding two.
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

### Measured 2026-09-24: the reference contract is value equality whenever `Cons` is upstream

`Read WKT` parses every frame and returns new geometry objects every frame (no cache; see
`IONodes.ReadWKT`). So `Read WKT → Geometries → Network` rebuilt every frame in the shortest-path
help patch — `Networks Built` read 1819 after thirty seconds. The six-pad `Read WKT ×6 → Cons →
Network` version had read 1, but only because VL's `CollectionBuilders` behind `Cons` keeps the old
items when the new ones are `Equals`, and NetTopologySuite's `Geometry.Equals` is value equality.
The contract above is therefore already value-based on that path and reference-based on every
other, which is a trap rather than a rule. The help patch now puts the parse in a `Cache` region
(the community idiom); the candidate node-level answer — reference check first, element-wise
`EqualsExact` before rebuilding — is on the ROADMAP for the user to decide, because it rewrites this
section and its tests.

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

## The experimental network — and why it is `NetTopologySuite.Experimental.Network`, not `NetTopologySuite.Network`

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

**Decided 2026-08-28: promoted to `NetTopologySuite.Network`, in place** — the review below happened
([NETWORK-SCOPE-PROPOSAL.md](NETWORK-SCOPE-PROPOSAL.md)): both named consumers were built and rung-4
verified in VL.Overworld the same day (`Prompt Which door`, `Prompt Grow a town`), both wanted the
surface unchanged, and the one pin a consumer asked for (`Max Snap Distance`) was added with the
refusal still measurable. The process node is `Network`, matching `SpatialIndex` — a process node is
named for the thing it holds. This package now contains exactly one algorithm of ours, this one, and
says so here; a second algorithm of ours wanting a home reopens the location question. The original
reasoning, kept:

Hence: the chapter was approved, a permanent public `NetTopologySuite.Network` was not — then. The code lives here because
the alternatives are worse — a new package created to resolve uncertainty about where code belongs
turns the uncertainty into a dependency, and a Dijkstra written in dataflow inside the chapter would
bury the lesson under its own implementation. It lives under `Experimental` so nobody mistakes it for
a promise. It is promoted, moved, or removed **after** three things exist: the chapter, the
abstraction that actually emerged from building it, and at least two more genuine consumers wanting
the same model (a building–entrance–street connectivity prompt, a procedural network prompt, an
accessibility experiment are the plausible ones). That review is a separate Network Package Scope
Proposal, not a line in this file. The evidence it will need — chapter, emerged abstraction, the API
that now looks useful, two consumers in outline — is in [NETWORK-SCOPE-EVIDENCE.md](NETWORK-SCOPE-EVIDENCE.md).

### The scope, as one sentence, asserted by 22 tests

> An undirected spatial network built from **explicitly** connected LineStrings in a local
> Cartesian space, with geometric length as cost and Dijkstra as the path algorithm; queries snap
> to the nearest node within an optional maximum distance, and the snap is always reported.

If a change would make that sentence false, the change waits for a new review, with
NETWORK-SCOPE-PROPOSAL.md as its template.

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
`Split` nodes moved here from VL.Mapsui, into a new `NetTopologySuite.Feature` category, and this package gained
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
- VL.GeoJSON's read-side `Split` and `GetProperty` **stayed where they were at first** —
  consolidating them here would give VL.GeoJSON a dependency on this package, which was a separate
  decision, not a rider on this one.

**That separate decision was taken on 2026-10-18.** Two `Split`s on the same `Feature` type, in two
packages, with different outputs, had no precedent among the 47 packs shipped with vvvv 7.4 — the
shipped packs repeat a node name only across *different* backends (`Serialize` in each
`VL.Serialization.*`). And "reference nobody" was already untrue: the published VL.Mapsui depends
on this package, so that its help patches open working. So:

- **`GetAttribute` moved here**, renamed from VL.GeoJSON's `GetProperty` to this package's own
  word for the table. It answers "one attribute by name" for a feature from *any* producer, with
  `Success` instead of the `ArgumentException` NTS's indexer throws.
- VL.GeoJSON becomes **VL.NetTopologySuite.GeoJSON**, depends on this package, drops its own
  `Split (Feature)` and `GetProperty`, and keeps only `Read GeoJSON` / `Write GeoJSON`. The
  dependency points one way — format package to geometry package, as `VL.Stride.TextureFX` →
  `VL.Stride` and `VL.IO.TUIO` → `VL.IO.OSC` do — and this package still references nothing of
  ours.

---

### A feature is a container, not a data model — built and taken out, 2026-11-07

One day, three cuts, all removed the same night, kept here because the reasons are the design.

**What was built.** The maintainer built a four-attribute feature out of four `Add` nodes and
asked for a table ("添加起来很麻烦啊，不能像表格一样添加"). Surveyed: GeoJSON's `properties` is an
untyped JSON object, NTS keeps an `AttributesTable`, both "name → any value", exactly VL's
Dictionary; vvvv has no editable Dictionary IOBox, but a Spread IOBox *is* an editable list. So
`Attributes (Join)` read two Spread IOBoxes as a table with CSV-style type inference, and
`Attributes (Split)` was the inverse with typed values. The maintainer then asked for a schema -
"a GIS feature is a row of a table whose columns are defined first", in vvvv a Record - and on
branch `record-bridge` the record *was* the feature: `Feature (FromRecord)` / `Record (FromFeature)`
read the user's record through `IVLObject.Type.Properties` (keys by `OriginalName`, writes with
`WithValue`, a generic `T` pin, the editor-vs-export shape caught by an editor-shaped test double),
first with exactly one Geometry field, then with a `Geometry Field` pin choosing among several,
the others riding along as attributes. Two help patches, 23 tests, every cut seen in vvvv.

**Why it all came out** (the maintainer, late that night, after reading NTS's own `Features`
library): a feature is what the map and the file *take* - NTS's `IFeature` is "a Geometry and a
description of its metadata", one geometry, a string-keyed bag, a bbox, an id, the GeoJSON object
in C#, with no schema and no rationale written anywhere; VL.Mapsui's `FeatureLayer` and `Pick`
and the GeoJSON package all take exactly that object. It is a transport container. The data -
a building with its outline, its entrance, its centre - lives in the user's own record, with as
many geometries as the thing has, and "one geometry per feature" is the exchange format's rule,
applied at the moment a feature is made for a layer. So:

1. **`Attributes (Join)` had a guess inside.** `"28"` became an integer and `"true"` a boolean;
   JSON tells `true` from `"true"`, and the maintainer had objected to exactly that collapse
   earlier the same day. vvvv's own `Add` chain carries each value with its type and guesses
   nothing. A few labels on a container do not need a table.
2. **`Attributes (Split)` was reachable by patching** - a ForEach over the dictionary with
   CoreLib's `KeyValuePair` Split - and this package's rule is that what a patch can wire is a
   help patch, not a node. The Feature viewer now does exactly that ForEach.
3. **The record bridge was the fragile part**: `IVLObject` reflection, a record with no CLR
   members in the editor and real fields after export (vl-mapsui's `ToFeatures`, 2026-08-15,
   removed 2026-09-25). A geometry package should not carry a dependency on VL's internals to
   provide a data model NTS itself does not have.
4. **The idiomatic answer needs no node.** A record can carry its own operations: the user writes
   `ToFeature` inside `Building` once - Split its fields, pick the geometry this layer draws,
   `Add` the labels that should travel - with no reflection and nothing guessed, and a second
   `ToFeature` for the entrance layer if they want one. That is a lesson for the tutorial
   (VL.Overworld), where "how to keep a dataset" belongs; it is not this package's surface.

**What NTS's own `Features` library is**, read on the night of the decision (the installed
2.1.0's XML docs and the repository's `develop` sources; the README is one line, *"An implementation
of Feature and FeatureCollection"*, and no file says why a feature has one geometry or names
GeoJSON, Simple Features or shapefile as its model):

| type | holds | their own words |
|---|---|---|
| `IFeature` | `Geometry`, `BoundingBox` (an optional `Envelope`), `Attributes` | "Contains a Geometry and a description of its metadata" |
| `Feature` | the one implementation, sealed | "Standard implementation of IFeature" |
| `IAttributesTable` / `AttributesTable` | a string-keyed bag of `object`: `Add`, `Exists`, `GetNames`, `GetValues`, `GetType(name)`, `GetOptionalValue`, indexer, `Count`; a `Dictionary<string, object>` underneath with an optional name comparer | "this feature's metadata, tagged by user-defined strings" |
| `FeatureCollection` | `Collection<IFeature>` plus a `BoundingBox` | "Represents a feature collection" |
| `IUnique` | an `Id` | "tagged by an identifier that's assumed to be unique" |

JTS has no Feature; NTS added this for its own readers and writers, split it into its own nuget at
2.0 (2019, dependency `NetTopologySuite [2.0, 3.0)` only; 34 packages depend on it; 2.2.0 is the
current version, this package pins 2.1.0). Geometry first, attributes as "metadata about it", no
schema, every value's type a per-value question. The maintainer's reading of their wording against
ours: *they say geometry plus the metadata that describes it; we say the record is the thing and
the geometry is one of its fields. Both hold, and the door is where one is translated into the
other.*

**How many geometries a feature carries, by system** - the survey behind "one geometry is the
exchange format's rule":

| system | geometries per feature |
|---|---|
| PostGIS table | any number of geometry columns (outline, centroid, entrance side by side is common) |
| GDAL/OGR, since 1.11 | several geometry fields, field 0 the primary |
| GeoPackage, Shapefile, ArcGIS feature class | exactly one |
| GeoJSON, KML Placemark | one (KML's `MultiGeometry` is the workaround) |
| Mapsui `GeometryFeature`, NTS `Feature` | one `Geometry` property, the rest in the attribute table |
| QGIS loading a PostGIS table | asks which column; one geometry per layer |

Storage allows many; every exchange and drawing layer takes one; the choice is made once, when the
feature is made for a layer. VL.Mapsui's `FeatureLayer` converts an NTS `Feature` to a
`GeometryFeature` with that one geometry, and `Pick` hands an NTS `Feature` back, so the NTS
`Feature` is the currency between this package, VL.Mapsui and the GeoJSON package, and a split of
the three Feature nodes into a package of their own (NTS's own split suggests it is legitimate)
would buy a version number to manage and rename no node; not done.

**The working model, in the maintainer's words** (2026-11-07, night): *"数据归数据… 一个 feature
在任何其他的系统里面，它可以带更多的 geometry… 只不过是在绘制的时候，或者是在我们做计算的时候，应该拿哪个
geometry 出来做，是一个需要选择的事情… Feature 就是一个运输容器，它不是数据模型… 相当于就是一个标签，很多标签
拿过去就可以了。"* Concretely: a dataset is the user's `Spread` of their own record, with every
geometry the thing has; the map does not care about features, it draws geometries and uses the
labels for styling and for what a click returns; so from one dataset the user makes as many kinds
of Feature as there are layers to draw (the outline layer, the entrance layer), each by an
operation on the record that picks the geometry and `Add`s the labels for that layer; a Feature
never goes back into the dataset. How to keep such a dataset - define the record, fill it, keep it
in one place, make features at the door - is the tutorial's subject (VL.Overworld), where the
maintainer wants "more care spent" on it than a package can give.

`Feature`, `Split`, `GetAttribute` are the whole category. The branch stays unmerged as the record
of what was measured (the `IVLObject` reading, `OriginalName` keys, the alphabetical property
order, a Geometry-valued attribute drawn in the Feature box); the only piece kept on `main` is the
Feature viewer listing every row.

## Drawing without a renderer — the HDE companion

**The problem (the maintainer, 2026-11-07):** WKT is text, and most people need to *see* a geometry
to understand what an operation did - but a Renderer in every patch is too heavy, and other packs
(VL.Fuse above all) show their values inside the IOBox. **The mechanism, measured in the vvvv 7.4
install:** the editor keeps a registry of *viewers*. `VL.UI.IValueViewer<T>` (in
`VL.UI.PatchEditor.Utils.dll`) has `Update(T value, ViewerContext, out IWidget)`; a widget's view
renders as a Skia `ILayer`, so every tooltip and IOBox preview is a small Skia picture drawn only
while it is on screen. A library registers one from a `RegisterServices` operation with the
`RegisterViewer` node (category `HDE.Viewers`, document `VL.HDE`), in a `<Package>.HDE.vl` placed
beside its package document - vvvv loads it by itself and compiles it to `<Package>_HDE_Plugin` in
its package-cache. That is what VL.Stride (`VL.Stride.HDE.vl`), VL.Audio, VL.IO.Redis and Fuse
(`VL.Fuse.HDE.vl`, seen in the cache as `VL.Fuse_HDE_Plugin`) do; the template is CoreLib's help
`HowTo Patch a Tooltip`. No Gray Book page documents it.

**What this package registers** (`VL.NetTopologySuite.HDE.vl`), drawn the way a GIS draws - the
maintainer's rule after the first probe: *a point is a pin on a map, not a disc; a polygon is
solid, and where its holes are must be visible.* `GeometryViewer` for every `Geometry`: `Outlines`
sorts it into rings, lines and points; every ring goes into one Skia path (a ForEach accumulator)
filled even-odd at 0.25 alpha so holes and multipolygons come out right, and stroked; lines are
stroked; points in a collection are circle markers sized at 2% of the bounds; Y is negated
throughout (geometry is y-up, Skia y-down); the result is scaled into the widget (first by the
editor's own `LayerViewer`, since 2026-11-08 by `ViewFrame`, below), so a lon/lat polygon of 0.01
degrees and a metre-grid polygon of 4,000 fill the box alike. `PointViewer` for a lone `Point` draws a map pin at a fixed size. `FeatureViewer`
shows a feature the way a GIS does on a click: the geometry drawn through the editor's
`AdaptiveViewer` (dispatch by runtime type, so a point feature gets the pin), the attribute table under it,
every row: a ForEach over the dictionary itself (CoreLib's `KeyValuePair` Split) whose body is the editor's own row shape (key in a
`FixedWidthWidget`, a `VerticalLineWidget`, the value), collected into a `Column`. The editor's
`DictionaryViewer` was tried first and is not used, because it takes `TooltipNumberOfItemsOfCollection`
rows - a user setting, 3 by default - and prints `...` for the rest; the maintainer saw a
four-attribute feature list three and called it disappointing, and a feature's attributes are the
one thing a GIS always shows in full. The rows are the editor's own `DictionaryViewer` row shape,
copied node for node from `HDE.WidgetsAndViewers.vl` (`FixedWidthWidget` key | `VerticalLineWidget`
| `Flexible` value, a `HorizontalLineWidget` between rows, both cells through `AdaptiveViewer`), so
a key is quoted, a number right-aligned, a boolean a checkbox, a colour a swatch - the maintainer
asked for the editor's look, after a first cut with text cells and a second with loose cells. Only
the `Take` is missing: every row, on purpose. A per-IOBox row count, like the `Configure` panel of
a native IOBox, is not reachable from a viewer (`ViewerContext` has no settings), and the
maintainer chose all rows over the global `TooltipNumberOfItemsOfCollection`. An `AttributesViewer` for a bare
`Dictionary<String, Object>` was registered, seen working, and taken out the same evening: it
proved that a package may register a viewer for a CoreLib generic instantiation and that the
editor then uses it for every IOBox of that type in any patch - and that is exactly why it is
gone. The editor's grid is the better viewer for a dictionary, and a geometry package has no
business replacing it machine-wide. This package registers viewers for its own types only.
Colours are Leaflet's defaults (`#3388ff`, fill opacity about 0.2), the look
most people know from web maps. The editor picks the viewer registered for the most specific
type.

**What the probes caught** on 2026-11-07, each visible in one screenshot and invisible in any
compile: `Coordinates` concatenates a polygon's shell and holes, so a polyline through it drew a
segment from the shell into the hole (hence `Outlines`); the first line drew upside down (hence the
Y flip); a point drew nothing, then drew as a disc that read as a polygon (hence the pin); outlines
alone did not say what was inside a polygon (hence the fill). **`vvvvc` cannot compile an HDE
document** (`VL.UI.Forms.dll couldn't be found`), so the only check is a probe document with
Geometry IOBoxes opened in the editor, plus the one line vvvv's log writes when such a document
has an error.

**Boundary of the companion:** it depends on `VL.HDE` and `VL.Skia`, which the package itself does
not - the nuspec declares neither, because every vvvv carries them and an exported application
never loads a `.HDE.vl`. The package's own rule (no rendering, no maps) is untouched: the companion
draws for the *editor*, not for a patch. Not done: a viewer for `Coordinate` - the generic one
lists X, Y and Z, which is what a coordinate is.

### The view: sticky, with a grid and a size label (2026-11-08)

The editor's `LayerViewer` fits the bounds of the layers it is given into the box, every frame.
For a picture of one value that is right; for a value being edited it hides the edit - the
maintainer made a Rectangle wider and saw it get lower, changed a Circle's radius and saw
nothing. So `GeometryViewer` no longer goes through `LayerViewer`; it draws into a view decided
by `ViewFrame`:

- **Sticky.** A fresh view is 1.25 times the shape in its binding direction, 4:3, centred. It stays while
  the shape fits inside it and fills at least a third of it, so a radius or a width being
  dragged changes the drawing, not the zoom. State is per viewer instance, and the editor keeps
  one per IOBox (measured: an LFO-driven width moved inside a still grid); a tooltip is a new
  viewer per hover, so a glance is always fitted.
- **A dot grid at a round step** (1, 2 or 5 times a power of ten, 4 to 10 cells) and a label
  `4 × 2 · grid 1`: the absolute size, which a map shows with a scale bar and which no amount of
  fitting can show. The label sits in the bottom-left corner, in the margin below a freshly fitted
  shape, and the grid leaves a hole for it.
- **4:3, filling the whole widget.** The first version (1.6 headroom) used about half the box,
  which the maintainer saw at once, and then asked why the strips left and right stayed empty.
  The editor's `LayerViewer` draws into a fixed 1.6 × 1.2 widget and *squares* the bounds it is
  given (bounds → Scale 1.05 → FixAspectRatio 1,1 FitOut → ToQuadTransformation → Invert → × 2 →
  InViewport): a 4:3 view came out smaller (59% of the widget), a square one filled 95% of its
  height and nothing more, and a grid drawn into the strips only widened the bounds and zoomed out
  (every Skia layer tried counts toward the bounds; only `Text` reports just its box). So
  `GeometryViewer` uses the pattern of VL.Skia's own help, `Example Particle Life.HDE.vl`:
  `LayerWidget (Stateful)` with the editor's size, 1.6 × 1.2, around a `TransformSRT` whose
  Scaling and Translation `ViewFrame` computes, so the 4:3 view lands exactly on the widget
  (origin top-left, y down, in the widget's units). Public nodes only; VL.Audio's HDE uses
  `LayerWidget` the same way. `PointViewer` moved to the same pattern before `0.0.6-alpha` shipped
  (the maintainer's pre-release review: a point IOBox on a checkerboard beside geometry IOBoxes on
  the tooltip's background, and an invisible frame steering someone else's fit), with a constant
  transform; no viewer of ours goes through `LayerViewer` any more.
- **Rejected: a copy of `LayerViewer`.** It was tried first (fit 4:3, no margin) and worked, but
  only with `IsFriend="true"` on the VL.Skia dependency, because it uses three VL.Skia internals.
  Of the nine HDE documents shipped with vvvv 7.4, none but VL.HDE's own declares a friend. The
  maintainer asked that the solution follow the usual way, so it went.

The arithmetic is C# (`ViewFrameNode.cs` and `ViewFrame.cs`, 19 tests, the stickiness negative-tested) rather than
patch logic, because the companion cannot be compiled by `vvvvc` and conditions in hand-written
patch XML are where mistakes hide. It is a node, so it is public, and it carries
`[Smell(SymbolSmell.Hidden)]` so the NodeBrowser does not list it - not `Internal`, which was
tried first and makes a node unresolvable from any other document. Its outputs are in Skia space
(y down), like everything else the viewer draws.

## GeometryEditor — drawing in a window of the editor

The maintainer, 2026-11-11: *nobody should have to type WKT; let me draw it, and take the result
from a pin*. The question that came first was whether a package may extend the editor that far,
and if not, why not.

### What a package may do to the editor, measured

Measured in the vvvv 7.4 install (the `*.HDE.vl` documents it ships, VL.HDE.dll and VL.Core.dll by
reflection), in four community packs on this machine, and on nuget.org (eleven `*.HDE` packages).
Three public mechanisms:

| mechanism | public API | who uses it |
|---|---|---|
| a drawn, read-only view of a value | `RegisterViewer` + `IValueViewer<T>` | Fuse, Elementa, OpenCV, our viewers |
| a window of the package's own | an `Application` in `<Pkg>.HDE.vl`: `Command` (menu entry + shortcut) → `WindowFactory` → `SkiaWindow`; `HDE.API.CurrentSelection` for what is selected | VL.HDE's `Template.HDE.vl`, VL.Kairos (keyframe editor), VL.Audio, Inspector |
| writing into the document | `IDevSession.Current.CurrentSolution.SetPinValue(path, pin, value).Confirm(kind)` (VL.Core, `VL.Lang.PublicAPI`) | VL.Stride `RenderWindow` (its bounds), CoreLib `PersistentState`, VL.ImGui |

One thing is **not** possible: an *editable* IOBox for our own type. The IOBox editors
(`EditorFactory`, `IValueEditor`, `ValueEditor<T>` in `VL.UI.Forms`) are internal, and the
Inspector's per-type editors are a fixed list. Nothing here is a question of permission. The
editor simply offers no extension point there. Reaching the internal types by reflection would
work until the next vvvv release, and that is not how packages are written
([memory: follow the usual pattern]). So the editor is a window.

### The shape: a node in the patch, a window in the editor

- **`GeometryEditor`** ([ProcessNode], `NetTopologySuite.Geometry`) has one input, `WKT`. It
  outputs the geometry and `Is Editable`. The drawing *is* that pin's value: text in the `.vl`,
  saved with the patch, typeable by hand, and the same in an exported app, where there is no
  editor. The window writes it with `SetPinValue(NodeContext.Path.Stack, "WKT", wkt)` and
  `Confirm(SolutionUpdateKind.DontCompile)`. `DontCompile` is every flag but `AffectCompilation`,
  so the write enters the undo history. It is the same value `RenderWindow` uses. The pin must
  stay unconnected, because a link overrides the pin's own value. The node does not re-read a
  value that equals what it last wrote, so a part in progress keeps its kind.
- **The window** is an `Application` in `VL.NetTopologySuite.HDE.vl`:
  `Command` ("Geometry Editor", Ctrl+Shift+W) → `ForEach` → `EnsureWindow`, and
  `WindowFactory` → `SkiaWindow` whose input is the hidden node `GeometryEditorWindow`. The
  selection's elements carry `Element.UniqueId`, and each `GeometryEditor` knows its own id from
  `NodeContext.Path`. A static registry pairs them. The two properties are read by name, because
  `ILiveElement` lives in an editor assembly this runtime package does not reference. When they
  do not match, the window keeps the editor it showed last, or takes the one made last.
- **The canvas is C#**: `GeometryEditorLayer`, a Skia `ILayer`. This is the maintainer's choice,
  over patching it in the HDE document (which `vvvvc` cannot compile) or a separate `.HDE`
  package. It is why the package now references `VL.Core.Skia` (compile-time only,
  `ExcludeAssets="runtime;native"`).

### The model: a drawing is parts

The first version that morning drew one part without holes. The maintainer asked the same day for
every geometry type the package has, so `GeometrySketch` is a list of parts. A part is a point,
a line or a polygon, and a polygon is a shell plus holes. The type written follows the parts:

| parts | written as |
|---|---|
| one | `POINT` / `LINESTRING` / `POLYGON` |
| several of one kind | `MULTIPOINT` / `MULTILINESTRING` / `MULTIPOLYGON` |
| different kinds | `GEOMETRYCOLLECTION` |

What was *read* also keeps its container. A MULTIPOLYGON stays Multi when parts are deleted down
to one, because a patch downstream may expect the type. A collection stays a collection. Every
editable WKT round-trips exactly.

Not editable, and shown read-only so nothing is lost:
- Geometry with Z: the window is 2D, and the writer would drop Z.
- A collection holding a multi-geometry or another collection: flattening would change its
  structure.
- `LINEARRING`: an implementation type.

`SketchSession` holds the tools as a state machine without Skia. It was redesigned on 2026-11-12,
after the maintainer moved a whole shape by accident while trying to add a point. The first Select
tool decided between select, drag vertex, drag shape and insert by a few pixels of hit-testing.
The rules now come from a survey of 15 editors (Mapbox GL Draw, iD, QGIS, ArcGIS, Leaflet-Geoman,
OpenLayers, JOSM, Figma, Illustrator, Inkscape, Excalidraw, tldraw, draw.io, PowerPoint, Keynote),
shown to the maintainer as a design page and approved:
- **Select first, then edit.** Only the selected part shows its vertices and a "+" at the middle of
  every edge. An unselected part cannot be reshaped by a stray press, and a press on its vertex only
  selects it.
- **"+" adds a point.** A click puts one at the midpoint and selects it; a drag pulls it out. A
  double-click on an edge remains as the expert route.
- **Moving a whole part is a mode.** The Move action (or M) enters it. Dragging moves the part,
  with its old place drawn dashed. Done or Enter keeps the move; Cancel or Esc restores. Outside
  that mode, dragging a body does nothing.
- **A drag starts past 6 px** (JOSM uses 5 px, Excalidraw 10 px). A click never moves anything.
- **Actions beside the selection.** `Actions()` gives Move · Hole · Delete, Delete point,
  Done · Cancel, or Done · Back one point · Cancel. A disabled action carries its reason: a
  triangle's point is refused ("needs at least 3 points"), rather than the 2026-11-11 rule of
  quietly deleting the whole part.
- **One hint line** (`HintText`) says what can be done now. The status bar adds x / y, the SRID,
  the type and the point count.
- **Undo / Redo in the window**, of every committed edit. A change from outside the window (typed
  WKT, Ctrl+Z in the patch) starts the history again and clears the selection.
- **Point / Line / Polygon / Hole** draw. A part being drawn is previewed only, and committed when
  finished, so one stroke is one undo step. A finished line or polygon returns the tool to Select
  with the part selected, as the maintainer chose. Point stays, for placing many. Hole is entered
  from a selected polygon's Hole action.
- **Dragging empty space pans**, like a web map.

Hit-testing uses the real mouse position. New and moved vertices use the snapped one: with Snap
on, hit-testing the snapped point missed everything. Model, session and view are covered by
`GeometrySketchTests`, `SketchSessionTests` and `SketchViewTests`.

### What it does not know yet

Units. The grid is in plain coordinate units, so a lon/lat drawing is in degrees, unscaled for
latitude. That waits for the CRS chapter ([ROADMAP](ROADMAP.md)): reprojection stays out of this
package (see [What this will never contain](#what-this-will-never-contain)). The editor will learn
to show what the units are and to draw lon/lat at the right aspect.

## What stays raw

Reachable through VL's raw .NET nodes, deliberately not wrapped. The test — *is this a common
geospatial concept that benefits from a VL-native node?* — answers no for all of it:

- **Infrastructure**: `PrecisionModel`, `CoordinateSequence`, `CoordinateSequenceFactory`,
  `NtsGeometryServices`. Not hidden, not wrapped, not silently altered.
- **Configuration objects**: `BufferParameters`, `EndCapStyle`, `IsValidOp`. Behind pins where they
  matter. A 4-input `Buffer` node to expose an end-cap enum on a rare path is two decisions wearing
  one node.
- **The long tail of `Geometry`**, shorter since tier 1 (2026-11-07) made `IsSimple`, `Reverse`,
  `Boundary`, `InteriorPoint`, `Relate`, `Envelope`, `NumPoints`, `ConvexHull`, `IsWithinDistance`,
  `SymmetricDifference` and the five remaining named predicates into nodes: `Normalized`,
  `PointOnSurface`, `EqualsTopologically`, `Normalize`, `GetOrdinates` and roughly thirty more stay
  raw until asked for.
- **Whole subsystems**: prepared geometry, `Quadtree`, `LinearReferencing`, `Triangulate`,
  `Precision.*`, most of `Operation.*` and `Algorithm.*`. (`STRtree` left this list on 2026-08-23
  and became `NetTopologySuite.Index`; its nearest-neighbour, removal and node-capacity APIs stay raw — the
  handle `SpatialIndex` returns *is* the NTS tree, so they are one raw .NET node away.)
- **WKB and GeoJSON.** See [ROADMAP.md](ROADMAP.md).

NetTopologySuite is large. Wrapping all of it would turn the node browser into an API dump, which is
the failure mode the brief's §28 names and which no user benefits from.

**But the name obliges something, measured 2026-10-18.** The 39 nodes call about 30 of NTS 2.6.0's
2,318 public methods, which is the meaningless number; the meaningful ones are **20 of the 64
members of `Geometry`** (31%) and **10 of 24 user-facing capability areas touched, 4 complete**.
The list at the top of this section — `IsSimple`, `Reverse`, `Boundary`, `InteriorPoint`, `Relate`,
the remaining predicates, simplification, linear referencing, affine transformation — is exactly
what a Shapely or PostGIS user expects a geometry package to have, so "until asked for" has been
asked for: [ROADMAP.md](ROADMAP.md#what-the-name-obliges--coverage-measured-2026-10-18) orders
them in three tiers by user expectation; **tier 1 landed on 2026-11-07** (55 nodes, 35 of 64
`Geometry` members, 55%). What stays raw after that is the machinery (`Noding`,
`GeometriesGraph`, `Planargraph`, `Mathematics`) and the configuration objects behind pins.

---

## The package boundary

Apply this whenever considering an addition:

| If it answers | It belongs in |
|---|---|
| How do I create or manipulate geometry? | **here** |
| How do I attach data to a geometry, or read it back? | **here** — `NetTopologySuite.Feature`, see [Where a feature lives](#where-a-feature-lives) |
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
Coordinates → LinearRing → Polygon → Buffer → NTS Geometry → Feature [NetTopologySuite.Feature]
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
  in `NetTopologySuite.Feature` is `NetTopologySuite.Features.Feature` — the NTS team's, not ours — and wrapping
  it is precisely what keeps this package from becoming the definition of the whole domain.
  Inventing a type here would do the opposite.
- **Generalised GIS abstractions** — `IGISGeometry`, `SpatialEntity`, `GISContext`, `GISDocument`.
  No current problem requires them.
- **A generic geospatial file-format package.** WKT is in scope because it is NTS's own IO. Letting
  every format follow is how a focused package stops being one.
