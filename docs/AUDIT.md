# Repository audit and MVP design — VL.NetTopologySuite

Written 2026-08-14, before any code. Answers §37 of the startup brief: current setup, what VL
already exposes, where the friction is, the smallest node set for the MVP, what stays raw, and the
Phase 1 plan.

Every behavioural claim below about NetTopologySuite was **measured** against NTS 2.6.0 by a
throwaway console probe, not read from documentation. The results are in
[Measured NTS behaviour](#measured-nts-behaviour), and two of them contradict what the sibling
repositories currently assert in prose.

---

## Contents

- [The finding that comes first](#the-finding-that-comes-first)
- [Current setup](#current-setup)
- [NTS exposure — what VL already gives you](#nts-exposure--what-vl-already-gives-you)
- [Measured NTS behaviour](#measured-nts-behaviour)
- [Friction points](#friction-points)
- [Proposed public node set](#proposed-public-node-set)
- [Architectural decisions, per node](#architectural-decisions-per-node)
- [What should stay raw](#what-should-stay-raw)
- [Phase 1 plan](#phase-1-plan)
- [Decisions taken](#decisions-taken)

---

## The finding that comes first

**The MVP described in §35 of the brief already exists, is already wrapped as VL nodes, and is
already published to nuget.org — in `VL.GIS 0.2.0-alpha`.**

`D:\2026_Projects\vvvv-gis\src\VL.GIS.Core\GeometryNodes.cs` and `SerializationNodes.cs` between
them ship roughly 40 nodes over NetTopologySuite under the categories `GIS.Geometry` and
`GIS.Serialization`. Mapped against the brief's own roadmap (§34):

| Brief's roadmap | Already in `VL.GIS 0.2.0-alpha` |
|---|---|
| `Coordinate` | ❌ **absent** — VL.GIS uses `(double longitude, double latitude)` tuples and never exposes NTS's `Coordinate` |
| `Point` | ✅ `CreatePoint`, `CreatePoint3D` |
| `LineString` | ✅ `CreateLineString` |
| `LinearRing` | ❌ **absent** — built internally by a private helper, never surfaced |
| `Polygon` | ✅ `CreatePolygon` (auto-closing), `CreatePolygonWithHoles`, `CreateBoundingBox` |
| `MultiPoint` / `MultiLineString` / `MultiPolygon` / `GeometryCollection` | ❌ absent |
| `Area` `Length` `Centroid` `Envelope` `Distance` | ✅ all five |
| `IsValid` `GeometryType` `IsEmpty` `NumPoints` `SRID` | ❌ absent |
| `Buffer` | ✅ `Buffer`, `BufferWithStyle` |
| `Intersection` `Union` `Difference` `SymmetricDifference` | ✅ all four |
| `Contains` `Intersects` `Within` `Touches` `Disjoint` | ✅ all five (plus `Covers`) |
| `Crosses` `Overlaps` | ❌ absent |
| `Read WKT` / `Write WKT` | ✅ `ParseWkt`, `TryParseWkt`, `ToWkt` |
| `WKB` (listed as "later") | ✅ already there — `ParseWkb`, `ToWkb`, `ParseHexWkb`, `ToHexWkb` |
| GeoJSON (§14, "treat cautiously") | ✅ already there — `ParseGeoJsonGeometry`, `ToGeoJsonGeometry` |
| Simplify, ConvexHull ("later") | ✅ already there |

So the honest framing of this repository is **not** "wrap NTS for VL" — that is done. It is one of:
re-cut an existing published surface along library lines, or replace it with a better-designed one.
Which of those it is changes the work materially, and was the first decision — resolved as
clean slate; see [Decisions taken](#decisions-taken).

Two further facts that bear on it:

**`VL.GIS/docs/DESIGN.md` explicitly rejects this package's name.** Under *Package boundaries*:

> Domain names for multi-library packages, library names for single-library ones. VL.GIS wraps
> four libraries, so it sits alongside `VL.Audio` and `VL.2D`, **not `VL.NetTopologySuite`**. A
> package named after a library should wrap exactly that library.

Creating `VL.NetTopologySuite` reverses a decision that repository settled deliberately and wrote
down. That is a legitimate reversal — the same document's *other* rule, "one package per wrapped
library", points the opposite way, and `VL.GIS.Core` wraps three libraries in one assembly
(NetTopologySuite, `NetTopologySuite.IO.GeoJSON`, ProjNet) in plain violation of it. The brief's
own §8 and §14 name `VL.ProjNet` and `VL.GeoJSON` as future packages, which is exactly a
decomposition of `VL.GIS.Core` by library. But it is a reversal, and it should be recorded as one
rather than discovered later.

**`VL.GIS 0.2.0-alpha` is on nuget.org and cannot be unpublished.** Anything decided here about
node names is a decision about whether existing patches keep working.

---

## Current setup

### This repository

Empty. No `.git`, no files, nothing but the directory. Everything below is greenfield.

### Toolchain present on this machine

| | |
|---|---|
| .NET SDKs | 5.0, 6.0, 7.0, **8.0.413**, 10.0.103 — 8.0 is the one that matters |
| vvvv gamma | 28 installs; newest is **7.4** (`C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64`) |
| NetTopologySuite | **2.6.0 is the current latest stable on nuget.org** (verified with `dotnet package search`; there is no 2.7 or 3.x). Already in the local NuGet cache. |
| Target framework | **net8.0** — set by vvvv gamma 7.x, not a choice |
| `VL.Core` floor | **2025.7.2** — the version that introduced `[Name]` and `[SkipCategory]`, both of which this package needs. 7.4 is installed. |

Both sibling repositories agree on all of the above, so there is nothing to reconcile.

### Conventions to inherit, not re-derive

`vl-mapsui\docs\RULES.md` is the distilled, transferable form of everything both repositories
learned the expensive way — 7 packaging rules, 4 runtime rules, and a measured node-design
section. **It should be copied into this repository verbatim**, which is what that document
itself instructs: *"a rule transfers; a note about another repository's file does not."*

The ones that shape this package specifically:

- `[assembly: ImportAsIs(Namespace = "VL")]` is mandatory. Without it the package loads, compiles,
  packs and exports with zero warnings and contributes **no visible nodes**. Nine VL.GIS releases
  shipped that way.
- An upstream library must be a **package in a package repository**, not merely a
  `<NugetDependency>` line — otherwise a node whose signature names `Geometry` is built with no
  working pins and every link to it is silently dropped. Since *every* node here mentions an NTS
  type, this package fails completely rather than partially if that is got wrong. It is the single
  highest-risk item in Phase 1.
- A `.vl` is UTF-8 **with** BOM; every `Id` is exactly 22 characters, first `[A-V]`.
- Three inputs is the target, five means two decisions are wearing one node (measured: 94% of
  VL.CoreLib's 901 static nodes take three or fewer).
- Fluent operations — return type equals first parameter type — name their output pin `Output`;
  everything else `Result`. `vvvvc` rejects the wrong one.

`public static` vs `[ProcessNode]` — the rule that cost a home network — barely bites here:
nothing in this package opens a socket, a file or a GPU resource. See
[GeometryFactory](#architectural-decisions-per-node) for the one place it is worth thinking about.

---

## NTS exposure — what VL already gives you

Category rule, from both sibling repos: `category = (.NET namespace minus the "VL" prefix) + type name`.

**Proposal: assembly `VL.NetTopologySuite`, root namespace `VL.NTS`.** That yields the category
`NTS` (and `NTS.Geometry`, `NTS.IO` … via `[Name]`), which is what §26 asks for, and it avoids a
`NetTopologySuite.Geometry.Point` that would be unreadable in the node browser. Decoupling the
assembly name from the namespace this way is exactly what both siblings already do —
`VL.GIS.Core` uses `RootNamespace = VL.GIS`, `VL.Mapsui` documents the decoupling as deliberate.

What VL gives you **for free**, once NTS is a resolvable package:

- **Every public NTS type appears on pins.** `Geometry`, `Point`, `Polygon`, `Coordinate`,
  `Envelope`, `GeometryFactory` all flow between nodes as opaque values with no wrapper written.
  This is the whole basis of §27's escape hatch and it needs no code.
- **Raw .NET reflection nodes** reach every NTS method and property, including everything this
  package deliberately does not wrap. They are hidden behind the NodeBrowser's dependency toggle
  rather than absent.
- **Spread ↔ .NET collections convert automatically.** A `Spread<Coordinate>` satisfies an
  `IEnumerable<Coordinate>` pin; VL's `Spread<T>` is backed by an immutable array. §20's question
  is already answered by the platform: **take `IEnumerable<T>` on inputs, return
  `IReadOnlyList<T>` on outputs**, which is what `VL.GIS.Core` does and what VL.CoreLib expects.
  No conversion helpers are needed.
- **`out` parameters become extra output pins**, and camelCase parameter names become "Camel Case"
  pin labels. XML doc comments become tooltips.

What VL does **not** give you, and is therefore the entire justification for this package:

- **Constructors are not nodes.** `new Point(...)` is unreachable; a `public static` method is
  required for every geometry you want to create. This is the single largest gap.
- **Overload sets are ambiguous.** `GeometryFactory.CreatePolygon` has five overloads
  (`Coordinate[]`, `CoordinateSequence`, `LinearRing`, `LinearRing + LinearRing[]`, none); the
  reflection nodes surface all of them with no guidance about which one a patch wants.
- **Optional and defaulted parameters** on the raw API do not read as pins with sensible values.
- **No node names the domain.** `BufferOp.Buffer(g, d, parameters)` is not `Buffer`.

---

## Measured NTS behaviour

Probe run against NetTopologySuite 2.6.0 on 2026-08-14. **The first block is the important one.**

### NTS geometries are *not* deeply immutable — measured

| Probe | Result |
|---|---|
| `Coordinate.X` has a public setter | **yes** |
| `factory.CreatePoint(c)` then `c.X = 777` → `point.X` | **777** — the factory did **not** copy |
| `lineString.Coordinates[0].X = 555` → `lineString.Coordinates[0].X` | **555** — `.Coordinates` returns **live** references, not copies |
| `point.Coordinate.X = 444` → `point.X` | **444** — `Point.Coordinate` is live |

**This contradicts what both sibling repositories currently assert.** `vvvv-gis/docs/DESIGN.md`
says *"Immutability, because NetTopologySuite is immutable — Operations return new geometries"*,
and `CLAUDE.md` says *"NTS geometries are immutable; operations return new objects."* The second
half of each claim is true and the first half is not: **operations are non-mutating, but
coordinate storage is shared and writable.** The default `CoordinateArraySequenceFactory` wraps
the caller's `Coordinate[]` rather than copying it.

Why this matters more in VL than in C#: a value flowing into two branches of a patch is *the same
reference*, and there is no `readonly` to stop the second branch writing through it. A patch that
holds a `Coordinate`, feeds it to `Point`, and later edits that `Coordinate` would silently move a
geometry that has already been drawn. §22 of the brief asked for exactly this to be established
before designing, and the answer changes the design — see
[Friction points](#friction-points) item 1.

### SRID

| Probe | Result |
|---|---|
| `GeometryFactory.Default.SRID` | `0` |
| `new GeometryFactory().SRID` | `0` |
| `Buffer`, `Centroid`, `Envelope` preserve SRID | **yes**, all three |
| `Intersection` of SRID 4326 with SRID 0 | **`4326`, no exception** — mixing SRIDs is silent |
| `new WKTReader().Read("POINT(1 2)").SRID` | **`-1`** — not 0 |
| `WKTReader` with `NtsGeometryServices(pm, 4326)` | `4326` |
| `Read("SRID=4326;POINT(1 2)")` | `4326` — EWKT prefix is honoured |

Two warts worth surfacing rather than hiding: **`WKTReader` yields SRID `-1`**, a third value
meaning "unset" alongside `0`, so a WKT-parsed geometry and a factory-built one do not agree; and
**mixing SRIDs never throws**, so a wrong answer from a wrong CRS looks exactly like a right one.
`VL.GIS 0.2.0-alpha` has this inconsistency live today: its `CreatePoint` yields SRID 4326 while
its `ParseWkt` yields −1.

### Ring closure and validity

| Probe | Result |
|---|---|
| `CreateLinearRing` on an unclosed ring | throws `ArgumentException: points must form a closed linestring` |
| `CreatePolygon` on an unclosed ring | throws the same |
| `CreateLinearRing` on 3 closed points (degenerate) | **does not throw** |
| bow-tie polygon `.IsValid` | `false` |
| `IsValidOp(bowtie).ValidationError` | `Self-intersection` at `(1, 1)` — message **and** coordinate |
| empty polygon `.IsValid` | **`true`** |

So closure is enforced at construction and validity is not — a constructible ring can still be an
invalid polygon. And `IsValidOp` gives a usable reason plus a location, which makes §10's "later"
validation node cheap enough to consider early.

### Empty geometries, Z and M

| Probe | Result |
|---|---|
| `CreatePoint(null)` / `CreateLineString([])` / `CreatePolygon(null)` | all produce a valid **empty** geometry, no throw |
| empty polygon `.Area` | `0` |
| empty point `.Centroid` | empty, no throw |
| `CoordinateZM` through the default factory | **Z and M both survive** (`3`, `4`) |
| `new WKTWriter().Write(point with Z)` | `POINT (1 2)` — **Z is dropped** |
| `new WKTWriter(3).Write(point with Z)` | `POINT Z(1 2 3)` |

Empty-in/empty-out throughout, which means the creation nodes can safely accept an unconnected
pin. And `WKTWriter` silently drops Z at its default dimension of 2 — a pin, not a default to
leave unexamined.

---

## Friction points

Ordered by how much they actually cost a patch author.

**1. `Coordinate` is mutable and shared, and nothing in the raw API defends against it.**
Measured above. The raw `factory.CreatePoint(c)` leaves the patch holding a live handle into the
geometry it just made. This is the strongest single argument for wrapper nodes rather than
reflection nodes, and the fix is one line per creation node — **copy the coordinates on the way
in** — which the raw API will never do for you.

**2. Constructors are not nodes.** Nothing can be created at all without a `public static` method
per geometry type. Unavoidable, and the reason Phase 1 is mostly creation nodes.

**3. `GeometryFactory` is in the way of the common case.** Every `Create*` method lives on an
instance, so the reflection-node route is `GeometryFactory → CreatePoint` for every point in every
patch — exactly what §7 says to avoid. `VL.GIS` solved this with a `private static readonly`
factory hardcoded to SRID 4326 and no way to reach it; that removes the friction and takes the
semantics with it, which §7 also says not to do.

**4. Polygon construction has five overloads and one of them is a trap.** `CreatePolygon(Coordinate[])`
throws on an unclosed ring, and "unclosed" is the natural thing for a patch author to produce —
four corners of a rectangle, not five. Auto-closing is a genuine convenience with a measured
justification, and §18 is right that it must be documented rather than silent.

**5. Acronym casing in node names is unverified.** VL splits PascalCase method names into pin and
node labels, so `ParseWkt` renders as **"Parse Wkt"** in VL.GIS today. Whether `ReadWKT` gives
"Read WKT" or "Read W K T" is **not established** — §25 asks for `Read WKT` and I have not
confirmed which C# spelling produces it. This needs the GUI, which is the only thing that proves a
node's label. Recorded as an open Phase 1 verification item, not guessed at.

**6. `Envelope` is two different things.** NTS has an `Envelope` *value type* (a bounding box, not
a `Geometry`) and `Geometry.Envelope`, a **property returning a Polygon**. `VL.GIS`'s `Envelope`
node returns the polygon; anyone wanting the four numbers has to use `GetBoundingBox`. Both are
worth having and they need names that do not collide — and per VL.GIS's own rule that *every
opaque value needs a reader node*, an `Envelope` value on a pin is useless without a `Split`.

**7. Tuples are not native types.** `VL.GIS` takes `IEnumerable<(double longitude, double latitude)>`
throughout. It reads acceptably in a patch, but it means NTS's `Coordinate` never appears, `Z` is
reachable only through a separate `CreatePoint3D`, and `M` is unreachable entirely. This is the
concrete way the existing package violates the brief's §3 core rule.

---

## Proposed public node set

Fourteen nodes. Every output is a native NTS type; no node exceeds three inputs.

### `NTS.Geometry` — creation

| Node | Inputs | Output |
|---|---|---|
| `Coordinate` | `X`, `Y` | `Coordinate` |
| `CoordinateZ` | `X`, `Y`, `Z` | `CoordinateZ` |
| `Point` | `Coordinate` | `Point` |
| `LineString` | `Coordinates` (spread) | `LineString` |
| `LinearRing` | `Coordinates` (spread) | `LinearRing` |
| `Polygon` | `Shell` (`LinearRing`), `Holes` (spread of `LinearRing`) | `Polygon` |

Two `Coordinate` nodes rather than one with an optional `Z`: these are two **native NTS types**
(`Coordinate`, `CoordinateZ`), not two conveniences over one type, so §39 is not violated. `M`,
`CoordinateM` and `CoordinateZM` are deliberately out of v1 per §17 — measured as working, so this
is a scope decision and not a limitation to hide.

**`Polygon` takes rings, not coordinates — a deliberate deviation from the §35 sketch.** The MVP
graph becomes `Coordinates → LinearRing → Polygon → Buffer`, one node longer than the brief draws
it. The reasons: it is Rule 4 (composition over giant nodes), it keeps `Holes` and `Shell` the same
type so the pins are learnable, and adding a second `Polygon` that takes coordinates directly is
precisely the duplicate-convenience family §39 forbids. `LinearRing` carries the auto-closing, so
the convenience is not lost — it is named. **Easily overruled if you would rather have the shorter
graph;** say so and `Polygon` takes `IEnumerable<Coordinate>` for its shell instead.

### `NTS.Geometry` — inspection

| Node | Inputs | Output |
|---|---|---|
| `Area` | `Geometry` | `Float64` |
| `Length` | `Geometry` | `Float64` |
| `Centroid` | `Geometry` | `Point` |
| `IsValid` | `Geometry` | `Boolean`, `Reason` (string), `Location` (`Coordinate`) |

`IsValid` gets three outputs rather than one because `IsValidOp` hands over the reason and the
coordinate for free (measured: `Self-intersection` at `(1, 1)`), and a bare `false` is the least
useful thing a validity node could say. Extra **outputs** cost a patch nothing — the three-input
rule is about inputs.

### `NTS.Operation`

| Node | Inputs | Output |
|---|---|---|
| `Buffer` | `Geometry`, `Distance`, `Segments` (=16) | `Geometry` |
| `Intersection` | `A`, `B` | `Geometry` |
| `Union` | `A`, `B` | `Geometry` |

Three operations, not the eight of §11 — enough to prove the geometry-preserving chain of §12
composes. The rest are one method and a doc comment each once the shape is settled.

### `NTS.IO`

| Node | Inputs | Output |
|---|---|---|
| `Read WKT` | `WKT` | `Geometry`, `Success` |
| `Write WKT` | `Geometry` | `String` |

`Read WKT` reports failure on a pin instead of throwing. A malformed string is the *expected* state
while someone is typing into an IOBox, and an exception 60 times a second is not a diagnostic. Exact
C# spelling pending the label check in [Friction points](#friction-points) item 5.

---

## Architectural decisions, per node

Answering §38 for the three that are not obvious. The other eleven are one-line wrappers whose
answers are all the same: no state, no mutation, native output, exists because a constructor or an
instance method is not reachable as a node.

### `Coordinate`

- **Problem** — nothing can make one; `new Coordinate(x, y)` is not a node.
- **Raw NTS API** — `new Coordinate(double, double)`.
- **Why raw is insufficient** — constructors are not nodes, full stop.
- **State** — none. **Mutation** — none, it creates.
- **The real decision** is not this node, it is what *consumes* it: since `Coordinate` is mutable
  and the factory does not copy (measured), **every creation node copies its coordinates on
  entry.** `Point`, `LineString` and `LinearRing` each take a defensive copy, so a geometry can
  never be changed through a `Coordinate` the patch still holds. This is the one place the package
  does something the raw API does not, and it is why these are wrappers and not reflection nodes.
- **Decision — wrap.**

### `GeometryFactory`

- **Problem** — §7. Requiring `GeometryFactory → Point` in every patch is unacceptable; hiding the
  factory entirely takes SRID and precision with it.
- **Raw NTS API** — `GeometryFactory.Default` (SRID 0, floating precision, measured), or
  `new GeometryFactory(PrecisionModel, int srid)`.
- **State** — a factory is a reusable object, but it holds **no unmanaged resource, no handle and
  no thread**, so rule 8 does not apply and it does not need `[ProcessNode]`. One shared
  `static readonly` default is correct and is what `VL.GIS` already does.
- **Decision — do not wrap in v1, and do not hide.** Creation nodes take an optional
  `GeometryFactory? factory = null` pin and fall back to the package default when unconnected.
  Unconnected is the only thing that can mean "the default" — that is rule 8 of the sibling
  `CLAUDE.md`, learned at the cost of 444 stray tiles. Advanced users reach
  `new GeometryFactory(...)` through the reflection nodes and plug it straight in; a dedicated
  `GeometryFactory` node can follow once someone actually needs one.
- **Which default SRID was the second decision — resolved as 0.**

### `Read WKT`

- **Problem** — §13. Also the cheapest way to get a test fixture into a patch.
- **Raw NTS API** — `new WKTReader().Read(string)`.
- **Why raw is insufficient** — it throws on malformed input, and a `WKTReader` instance would have
  to be constructed by the patch.
- **State** — a `WKTReader` is reusable and holds no resource, so a shared `static readonly`
  instance is fine and matches `VL.GIS`. **One caveat carried, not resolved:** NTS's
  `NtsGeometryServices` caches internally and I have **not** established that sharing one reader
  across VL's threads is safe. VL evaluates a patch on one runtime thread, so this is not a v1
  hazard, but it is not a proof either — recorded rather than assumed away.
- **Mutation** — none. **Output** — native `Geometry`, plus `Success`.
- **Decision — wrap**, with the SRID that a parsed geometry carries made explicit rather than left
  at NTS's `-1`.

---

## What should stay raw

Reachable through VL's reflection nodes, deliberately not wrapped. §28's test — *is this a common
geospatial concept that benefits from a VL-native node?* — answers no for all of it:

- `PrecisionModel`, `CoordinateSequence`, `CoordinateSequenceFactory`, `NtsGeometryServices` —
  infrastructure. §23: do not hide, do not wrap, do not silently alter.
- `IsValidOp`, `BufferParameters`, `EndCapStyle` — reachable, and behind pins where they matter.
  `VL.GIS`'s `BufferWithStyle` is a 4-input node for what one enum on a rare path buys.
- Prepared geometry, `STRtree` / `Quadtree`, `Operation.*`, `Algorithm.*`, `LinearReferencing`,
  `Triangulate`, `Precision.*` — every one of them is "later" in §34 and none has a use case yet.
- `Geometry`'s own ~60 properties and methods. `IsSimple`, `Normalized`, `Reverse`, `Boundary`,
  `InteriorPoint`, `PointOnSurface`, `Relate`, `EqualsTopologically` — all present, none wrapped
  until asked for.
- **GeoJSON and WKB stay out of v1 entirely.** Both already exist in `VL.GIS`, and §14 is explicit
  that GeoJSON may deserve its own package. Adding either here before the core is stable would be
  the "generic geospatial file-format package" §14 warns against.

---

## Phase 1 plan

The smallest milestone that proves the architecture. **One variable at a time** — the sibling
repositories lost nine releases to changing the `.vl`, the csproj and the nuspec in one round.

**Step 0 — settle the two decisions.** ✅ Both taken; see [Decisions taken](#decisions-taken).

**Step 1 — one node, end to end, before any others.** `Point(Coordinate)` alone: csproj with
`net8.0` / `VL.Core 2025.7.2` / `NetTopologySuite 2.6.0`, `AssemblyInfo.cs` with `ImportAsIs`, a
hand-written `.vl` with a BOM and a `NugetDependency` on NetTopologySuite, a nuspec, `build.ps1`
staging `deps\`, `pack.ps1`.

This step exists on its own because **every node in this package mentions an NTS type in its
signature**, so if NTS is not resolvable as a package the entire package contributes nothing and
`vvvvc` still exits 0. That failure is invisible and it is the one most likely to happen. Proving
it with one node costs an afternoon; discovering it with fourteen costs the fourteen.

Gate: `Coordinate → Point` appears under `NTS` **in the GUI**. Nothing short of the GUI proves a
node's category or its label — which also settles the acronym question in
[Friction points](#friction-points) item 5.

**Step 2 — the rest of creation.** `Coordinate`, `CoordinateZ`, `LineString`, `LinearRing`,
`Polygon`, each copying its coordinates on entry. Tests alongside: ring closure, that auto-closing
does what it says, that a mutated input `Coordinate` **cannot** move a finished geometry (the
regression test for the measured finding — and one that fails if the copy is removed, per *a check
that has never gone red is not a check*).

**Step 3 — `Buffer`, then the MVP graph.** `Coordinates → LinearRing → Polygon → Buffer → Geometry`
in a real patch, with SRID asserted at both ends.

**Step 4 — `Read WKT` / `Write WKT`.** `WKT → Read WKT → Geometry`, round-tripped.

**Step 5 — inspection.** `Area`, `Length`, `Centroid`, `IsValid`, against known values: unit square
area 1, perimeter 4, centroid (0.5, 0.5), the bow-tie invalid with reason `Self-intersection`.

**Step 6 — `Intersection`, `Union`.** Enough to show §12's chain composes without conversions.

**Step 7 — interop, outside this package.** The VL.Mapsui hand-off gets verified in a patch that
lives in **neither** repository, per §30 and the precedent already set by
`vvvv-gis\examples\Example Map with data on it.vl`. A patch needing two packages cannot ship inside
one whose dependencies do not guarantee the other.

**Then:** four help patches (`01 Create a Point`, `03 Create a Polygon`, `04 Read WKT`,
`06 Buffer Geometry`), `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, and `docs/RULES.md`
copied across.

Not in Phase 1: Multi* and `GeometryCollection`, the remaining predicates, `Envelope`, `SRID`,
`GeometryType`, WKB, GeoJSON, `GeometryFactory` as a node, M ordinates.

---

## Decisions taken

Both were settled on 2026-08-14, after this audit was written and before any code. **The sections
below are kept as they were written**, because a verdict is only as good as the premise it was
measured under, and the options that were rejected are part of the record.

### 1. Relationship to `VL.GIS` — **clean slate, ignore it as a constraint** ✅

VL.GIS is an earlier package whose structure predates a clear package-boundary strategy, and this
package is not to inherit those decisions. Concretely:

- **No dependency on VL.GIS**, in either direction.
- **No compatibility aliases, no migration infrastructure.** Not now.
- **No preservation of `GIS.Geometry` naming.** Design from the NTS conceptual model instead.
- **Do not mechanically recreate its ~40 nodes.** For each one, the question is not "how do we
  migrate this?" but **"would we independently design this node this way today?"** — reuse the idea
  where yes, ignore it where no.
- What becomes of VL.GIS — refactor, deprecate, umbrella package, or leave as a legacy experiment —
  is **a separate decision for after this package is stable and proven.** Not pre-solved.

The four places the answer came back "no", which are the substance of the redesign, are recorded in
[ARCHITECTURE.md](ARCHITECTURE.md#vvvv-gis-vlgis--a-reference-not-a-constraint).

Scope note that came with the decision: the Multi\* types and `GeometryCollection` are **in** the
first surface rather than deferred, because they are part of the NTS conceptual model the package is
built around. Inspection and topology operations stay limited to "the most useful".

### 2. Default SRID — **0, meaning unset** ✅

NetTopologySuite's own default. The `Factory` pin on every creation node is the way to say
otherwise, and the `GeometryFactory` node makes that discoverable by patching. Reasoning and the
`WKTReader` -1 correction are in
[ARCHITECTURE.md](ARCHITECTURE.md#the-factory-and-why-srid-is-0).

---

## The questions as they were asked

### 1. What is this package's relationship to the published `VL.GIS 0.2.0-alpha`?

The overlap is ~40 nodes and the name reverses a written VL.GIS decision. Three readings, each a
different job:

- **Extract** — `VL.GIS.Core` drops NetTopologySuite and depends on this package; `VL.ProjNet` and
  `VL.GeoJSON` follow, and `VL.GIS` becomes the domain layer over three library packages. Honours
  "one package per wrapped library". Needs a migration story for `GIS.Geometry` node names, since
  patches using them exist in the wild.
- **Replace** — this becomes the NTS surface, `VL.GIS`'s geometry nodes are deprecated in place.
  Free design, no compatibility constraint, two overlapping node sets in the browser for a while.
  (Technically fine: both would use NTS 2.6.0, and the whole 2.x line carries assembly version
  `2.0.0.0`, so there is no loader conflict — unlike the BruTile 5-vs-6 problem between VL.GIS and
  VL.Mapsui.)
- **Parallel** — greenfield, ignore the overlap, let it resolve later. Fastest to start, and the
  reading under which the duplication is someone's problem eventually.

### 2. What SRID should a geometry built with the default factory carry?

- **`0`** — NTS's own default (measured). Truthful: "unset". Correct for a package named after the
  library, since NTS geometry is not inherently geographic. Costs a patch an explicit step to get
  a CRS onto it.
- **`4326`** — what `VL.GIS` hardcodes, and the assumption VL.Mapsui's pipeline is written around
  (`VL.Mapsui`'s `Feature` node documents "geometry in WGS84 longitude and latitude"). Convenient,
  and a claim about the numbers that the package cannot check.

Whichever it is gets documented on every creation node, because mixing SRIDs **never throws**
(measured) — a wrong CRS produces a confident wrong answer. And per §8, no node will imply that
setting an SRID reprojects anything: it does not.
