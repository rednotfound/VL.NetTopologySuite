# Network Package Scope Proposal — 2026-08-28

The review of 2026-08-23 approved Tutorial 13 and explicitly did **not** approve a permanent public
API for the network nodes, parking them under `NTS.Experimental.Network` until four pieces of
evidence existed: the chapter working, the abstraction that emerged from building it, the API that
now looks useful, and at least two further genuine consumers wanting the same model. All four exist
and are rung-4 verified ([NETWORK-SCOPE-EVIDENCE.md](NETWORK-SCOPE-EVIDENCE.md)): Tutorial 13
(2026-08-23), `Prompt Which door` and `Prompt Grow a town` (both 2026-08-28, in VL.Overworld). This
is the proposal that review asked for. **It is a proposal; the decision is the user's**, and the
open points are marked ⊙.

## 1. Location — promote in place

**Proposed: the nodes stay in VL.NetTopologySuite.** The alternatives, and why not:

- **A separate package** (`VL.SpatialNetwork` or similar). The code is ~200 lines; every input and
  output is an NTS geometry; every consumer in this family hands it NTS geometries and draws the
  NTS geometry it returns. A fourth repository for that is a maintenance cost (its own pack.ps1,
  tests, help, publish ordering) with no user benefit — and this family already retired one package
  (VL.GIS) for being a container without an identity. The evidence file's own lean, held since
  2026-08-23, is promote-in-place.
- **Stay experimental.** The bar the review set has been met; leaving the category `Experimental`
  after that would make the word mean nothing the next time it is used.

The honest tension stays on record: the algorithm depends on NTS only for `LineString`, `Point`,
`Coordinate` and `Length`, so it is not an *exposure* of NTS — ARCHITECTURE.md gets a paragraph
saying plainly that this is an algorithm of ours over NTS types, the first and only one, and that
the second algorithm of ours wanting a home reopens the location question with this paragraph as
evidence.

## 2. Identity — category `NTS.Network`, and ⊙ one rename

Category `NTS.Experimental.Network` → **`NTS.Network`**, joining `NTS.Index` as the second
capability category beside the geometry core.

⊙ **Node name: `BuildNetwork` → `Network`.** The package's own precedent is that a process node is
named for the thing it holds, not the act of building it — the STRtree node is `SpatialIndex`, not
`BuildIndex`; VL.CoreLib's are `FrameDelay`, `S+H`, not verbs. `ShortestPath` keeps its name (it is
a query, and a verb phrase is right for a step — the same convention the subpatch survey found in
shipped patches). Cost: a rename touches every consumer anyway (the category moves), so this is the
one moment a rename is free.

## 3. Surface — unchanged, plus the one pin a consumer asked for

```
Network      [ProcessNode]  Line Strings → Result, Node Count, Edge Count, Networks Built
ShortestPath [static]       Network, From, To, ⊙ Max Snap Distance
                            → Result, Length, Found, From Snap Distance, To Snap Distance
```

⊙ **`Max Snap Distance` (Float64, default `∞`) on `ShortestPath` — proposed: add.** It is the only
pin either consumer has wanted in three chapters (`Prompt Grow a town`: with unbounded snapping a
connected network can never answer `Found = false`, and the chapter had to seed two components to
show a flip). Semantics: if either snap distance exceeds the maximum, the result is `Found = false`,
empty path, length 0 — **and the snap distances are still reported**, because the doctrine is
"snapping is visible, never silent" and a refusal you cannot measure would break it. Default
infinity keeps all three existing chapters byte-for-byte in behaviour. The scope sentence gains
four words (below). Tests: over-tolerance from, over-tolerance to, exactly-at-tolerance (inclusive),
default-infinity unchanged.

**`Nearest Node` — proposed: leave out.** §3 of the evidence file proposed it; both consumers then
managed without it (`Which door`: the returned path's first vertex *is* the snapped node, gated by
`Count > 0`). One documented hole — an empty path when door and cursor share a node — is not worth
a node no consumer has needed. It stays on the roadmap as the first candidate when a consumer
actually cannot proceed.

## 4. The scope sentence, amended by four words

> An undirected spatial network built from EXPLICITLY connected LineStrings in a local Cartesian
> space, with geometric length as cost and Dijkstra as the path algorithm; queries snap to the
> nearest node **within an optional maximum distance**, and the snap is always reported.

If a change would make that sentence false, it waits for a new review — unchanged rule.

## 5. Non-scope — unchanged, verbatim from ROADMAP.md

One-way edges, turn restrictions or penalties, speeds, travel time, custom costs, road classes; A*,
heuristics, k-shortest paths, isochrones, contraction hierarchies; nearest-point-on-edge snapping,
edge splitting, tolerance snapping (note: `Max Snap Distance` bounds *query* snapping; it does not
introduce tolerance into *connectivity*, which stays exact shared endpoints), map matching;
automatic noding, topology repair, fuzzy endpoints; OSM anything; Z-aware connectivity; a generic
graph framework; geodesic weights or CRS transformation.

## 6. What the promotion touches

| where | what |
|---|---|
| `src/VL.NetTopologySuite/NetworkNodes.cs` | namespace comment, `Category = "NTS.Network"`, ⊙ `[ProcessNode(Name = "Network")]`, the new pin + its guard, XML docs |
| `test/…/NetworkTests.cs` | rename, + 4 tolerance tests (18 → 22) |
| `docs/ARCHITECTURE.md` | the "algorithm of ours" paragraph; the per-node decisions move out of "experimental" wording |
| `docs/ROADMAP.md` | the experimental paragraph becomes a pointer to this file; `Nearest Node` listed as first candidate |
| `docs/NETWORK-SCOPE-EVIDENCE.md` | a one-line header: the proposal exists, decision recorded below it |
| VL.Overworld | `Tutorial 13`, `Prompt Which door`, `Prompt Grow a town`: category string (and ⊙ node name) — edit in place, then all four rungs; Tutorial 13 and Which door are layout-arranged, so anchored single-occurrence edits only |
| vl-mapsui / vl-geojson | nothing |

Nothing is published, so no version consequence.

## 7. Decision record

Decided by the user, 2026-08-28, all three as recommended: **promote in place** as `NTS.Network`;
**rename `BuildNetwork` → `Network`**; **add `Max Snap Distance`** (default ∞, refusal reported).
`Nearest Node` stays out. Implemented the same day: source, 22 tests, three VL.Overworld chapters
repointed, all four rungs.
