# The experimental network — evidence for a scope decision not yet made

Written 2026-08-23, the evening `NTS.Experimental.Network` and VL.Overworld's Tutorial 13 both passed
their fourth rung. This file exists because the review that approved the chapter did **not** approve
a permanent public API, and asked for four things before that question is reopened: the chapter,
the abstraction that emerged from building it, the public API that now looks useful, and at least
two plausible future consumers. Here they are. **Nothing in this file is a decision.** It is the
input to a separate Network Package Scope Proposal, if and when one is written.

---

## 1. The chapter, as built

`Tutorial 13 Close does not mean reachable` — a hand-typed town in a local Cartesian space declared
as metres, a river, one bridge, an orange dot on the north bank, the cursor as destination. Two lines
and two numbers: straight (`Distance`, ~200 m) and along the streets (`ShortestPath`, ~800 m). One
toggle, `Bridge Closed`, swaps the MultiLineString for the same one without its last line: the teal
path vanishes, `Found` reads False, the grey line does not move a pixel, `Networks Built` ticks.

What it took: **7 nodes of network machinery** (`Read WKT` ×2, `Switch (Boolean)`, `Geometries`,
`BuildNetwork`, `ShortestPath`, `Distance`) and about sixty of drawing and conversion. The two
experimental nodes did exactly what the design asked and nothing the design had to work around.
That is the first, weak piece of evidence: **the abstraction fit the lesson without special cases.**

## 2. The abstraction that emerged

Smaller than the design, in every dimension where a choice existed:

| dimension | what emerged | what was designed and dropped |
|---|---|---|
| graph | undirected, edge = one LineString, node = its two endpoints only | interior vertices as nodes; tolerance; auto-noding |
| cost | `LineString.Length` | a cost pin |
| query | two Points, snapped to nearest node, snap distances exposed | arbitrary Geometry; nearest-point-on-edge |
| lifecycle | topology retained (`BuildNetwork` is a process node); path stateless | `Paths Computed` counter |
| result | `Path` (original edge geometry, reversed as needed), `Length`, `Found` | — |
| change detection | element-wise reference equality, shared with `SpatialIndex` (`InputSets`) | — |
| implementation | adjacency list + `PriorityQueue`, ~150 lines including docs | a `PlanarGraph` subclass |

One thing emerged that was *not* designed: **the change-detection rule turned out to be shared, and
was factored into `InputSets`.** Two stateful nodes now make the same promise in the same words. If a
third arrives (prepared geometry is the obvious one), that helper is the pattern. This is the
strongest structural fact the experiment produced, and it is about the package's stateful nodes in
general, not about networks.

The scope sentence held: *an undirected spatial network built from explicitly connected LineStrings
in a local Cartesian space, with geometric length as cost and Dijkstra as the path algorithm.*
Nothing during the build tempted a change to it — which is evidence that the sentence is at the
right altitude, and also evidence that one chapter is too little to stress it.

## 3. The public API I would now propose — if one is proposed

Exactly the experimental surface, renamed and with one addition:

```
Network      [ProcessNode]   LineStrings → Network, Node Count, Edge Count, Networks Built
ShortestPath [static]        Network, From: Point, To: Point → Path, Length, Found,
                             From Snap Distance, To Snap Distance
```

The one addition: **`Nearest Node`** (`Network`, `Point` → `Point`, `Distance`), because both
consumers below want to *show* where snapping landed, and today they can only read the distance.
It is the smallest node that makes snapping fully visible rather than only measured.

What I would **still** leave out, and for the same reasons as today: costs, direction, A*,
edge-splitting, noding, tolerance, anything OSM. Each of those is a real domain with its own
contracts, and the moment one arrives the sentence above stops being true.

Where it would live is the open question, and the honest answer is that **the experiment did not
settle it.** Three observations bear on it:

- The code depends on NTS only for `LineString`, `Point`, `Coordinate` and `Length`. It would compile
  against any geometry type with those four things. That argues *against* `VL.NetTopologySuite`.
- Its inputs and outputs are NTS geometries, and every consumer of it in this family will hand it NTS
  geometries and draw the NTS geometry it returns. That argues *for* staying next to NTS.
- The package's own ARCHITECTURE.md says it wraps NetTopologySuite; ROADMAP.md's *Never* list names
  "generalised GIS abstractions". A shortest path is closer to that line than anything else in the
  package.

My current lean, held loosely: **if two more consumers arrive and want this exact surface, promote it
in place as `NTS.Network` with a paragraph in ARCHITECTURE.md saying plainly that it is an algorithm
of ours over NTS types**, because a separate package for ~200 lines is a maintenance cost with no
user benefit. If a consumer arrives wanting costs or direction, that is the signal for a package
with its own identity, not for growing this one.

## 4. Two plausible future consumers

Both are things this family has already talked about wanting; neither is invented for this file.

### 4a. `Prompt Which door` — building entrances to the street

A building footprint (polygon), its entrance points, and the street network around it. Question:
*from this entrance, how far is the nearest bus stop by the streets?* The hand-typed-town discipline
from Tutorial 13 does the streets; `Nearest Node` snaps the entrance; `ShortestPath` answers. This
is the pedestrian-access question every planner asks, and it uses the experimental surface **exactly
as it is** — plus `Nearest Node`, which is why that node is in §3.

It also stresses the connectivity contract in a useful way: an entrance is not on the network, and
the prompt has to say so out loud (the snap distance is the honest number). If the abstraction
survives that, it is more than a toy.

**Built 2026-08-28, rung 4 passed the same day** — as `Prompt Which door` in VL.Overworld, with one change
from the outline above: the destination is the cursor, not a bus stop, so the question is asked
three times a frame (once per door) and the totals compared. It used the experimental surface
**exactly as it is**. `Nearest Node` turned out NOT to be needed: the returned path's first vertex
is the snapped node, so the stub is drawn from it; the one case that cannot show — an empty path,
door and cursor on the same node — is gated with `Count > 0`. That is the exact hole `Nearest
Node` would fill, and one consumer is not enough to say it must. Streets came from a GeoJSON file
rather than a WKT box, and `Networks Built` still held at 1: the reference rule survives a second
producer. Full record in VL.Overworld's `docs/ACT-III-DESIGN.md`, "Second consumer".

### 4b. `Prompt Grow a town` — a procedural street network

Chapter 05 grew shapes; this grows a network: a few seed streets, a rule that extends dead ends and
sometimes joins them, a `BuildNetwork` every generation, and a `ShortestPath` between two fixed
points whose length **shrinks as the town grows** — the emergent behaviour is the whole prompt.
Creative-coding readers know this territory (L-systems, space colonisation); GIS readers know it as
network analysis. It stresses the *lifecycle* half of the contract: `Networks Built` climbs by design,
once per generation, which is the counter doing its job rather than reporting a fault.

### What both consumers do NOT need

Costs, direction, turn rules, OSM. Both want the sentence in §2 unchanged. That is the pattern the
review asked to see — *"several genuine uses naturally want the same abstraction"* — and it is
present in outline, and since 2026-08-28 **one of the two is present in code** (4a). Until the
second is built and passes its fourth rung, this file is half a prediction, and `NTS.Experimental`
stays exactly where it is.
