---
status: SPEC, not built. Written 2026-11-11 from an owner Q&A round (answers in section 1).
reference: Tasks/Claude/ExampleZoomOutFasterTravel.png
inputs: Memories/Code/Terrain.md (what exists), Tasks/Verification/Terrain_System.md (master spec), Tasks/NewPlans/TerrainEditorRedesign_System.md (editor rules, owned by another session), Docs/AnimationToolkit/EditorStyleGuide.md
---

# World Features: forests, farmland, villages and rail as shapes you draw

## 0. Context: why the world does not look like the picture yet

The reference picture reads as a place because it is built from **shapes with crisp edges**: forest masses, a patchwork of field parcels, lines of trees along roads and field borders, a few clustered buildings, a rail line with a crossing, a river with a bridge. The camera, tilt-shift, sun and far-view ground are already built and are not the gap.

What the terrain tools make today is **noise**: the generator writes fbm heights and a noise biome mask, and scatter follows that mask. Roads, rivers, lakes and crossings are the only shapes. So the world reads as texture, and nothing generated can be grabbed and moved afterwards.

The plan in one line: **everything that gives the land its meaning becomes a Feature (a spline or outline in the Scene view), every generator only ever creates Features, and one baker turns Features into the per-tile layers that already exist.** Roads and rivers already work this way; this spec extends the same pattern to areas, rail and settlements, then adds stamps and a world sketcher on top.

```
Base terrain (heights + biome noise)      exists: TerrainGenerationProfile, TerrainRegion
   + Features (vector, persistent scene)  roads, rivers, lakes, crossings exist; areas, rail, stations, villages NEW
   + Stamps (recipes that create Features) NEW
   + Hand overrides (sculpt, paint, locked instances, anything you moved)  exists, extended
        |  bake per tile (debounced, open tiles, stale list)   same pattern as TerrainRoadBaker
        v
Tile layers: height deltas, masks, draped meshes, props, card batches, spawn markers, nav
```

## 1. Owner answers (2026-11-11, do not re-ask)

- **WF-D1 Spawning:** both stamps and a whole-world sketch, **stamps first**. Whatever is spawned stays editable shapes.
- **WF-D2 Trees are billboards**, and the camera feature that does the billboarding must be respected (see WF-D8). **Trees can be cut down and respawn after a period**; the design must leave room for scattered objects to be interactable.
- **WF-D3 The player can ride trains.** Rail is not scenery. The riding gameplay gets its own spec (section 8); this spec builds the track, stations and the rail graph it needs.
- **WF-D4 Villages are buildings plus people markers**, and the owner goes in afterwards to add shops, move markers and make the place livelier. So everything a village stamp makes must be ordinary, movable scene objects, and a hand edit must survive a regenerate.

## 2. Orchestrator decisions (final unless the owner overrides)

- **WF-D5 Features live in the persistent world scene** (as `TerrainRoad` does), never in a tile scene, because they cross tiles. Their baked products live in the tile scenes and tile data assets so they stream.
- **WF-D6 One ownership rule everywhere: generated until touched.** Every generated object carries `TerrainGeneratedMarker { sourceFeatureId, locked }`. Moving, rotating, scaling or editing it in the Editor sets `locked`; Regenerate keeps locked objects and plans around them (the scatter planner already steers around locked instances). An object the owner adds by hand has no marker and is never touched. "Detach" on a stamp turns all its children into hand-owned objects.
- **WF-D7 A stamp is a parent Feature holding `recipe + seed + outline`;** its children are plain Features (areas, roads, lines) and objects. Stamps never write pixels or heights themselves. The world sketcher (section 7) only places stamps and lines, so its output is editable with the same tools.
- **WF-D8 Tree cards are upright quads that stay upright.** `CameraWorldLean` leans the whole rendered world by camera pitch so meshes and sprites share a plane; a card that also pitches toward the camera leans twice (`MonoBehaviours.md`, `VisualBillboardSystem`). The game's yaw is fixed, so a card is a static quad facing the fixed camera yaw: no per-frame billboard work at all. Cards cast shadows two-sided (the fixed sun shines from behind them toward the camera). Spike WF-S1 proves the look at both zooms before any tree content is made.
- **WF-D9 Two vegetation tiers, chosen per scatter rule.** `Dressing` (grass tufts, bushes, hedge runs, far trees): baked per tile into one card-batch mesh, one draw per tile per atlas, no entities, not interactable. `Interactable` (trees, rocks worth harvesting): one baked entity each with `ResourceNode`, reusing `Prefabs/ResourceNodes/Tree.prefab` and `MinionGatherSystem` unchanged.
- **WF-D10 Interactable count is budgeted per tile by `TerrainPlatformProfile`** (new field `maxInteractablePropsPerTile`; Switch keeps its 400 decals-plus-props line). Over budget, the planner demotes forest-interior trees to the card batch and keeps the edge band interactable, and the Tiles panel says how many were demoted. Promotion of a card to an entity on demand is **not** built now; the slot key in WF-D11 leaves room for it.
- **WF-D11 Cut and respawn is a ledger keyed by position hash,** copying the spawn-marker ledger (`TerrainSpawnSystem`: key, game time, `respawnAfterGameDays`). Entry pattern: one singleton buffer `HarvestedNodeLedger` created in a non-Burst `OnCreate`. Systems `HarvestedNodeRecordSystem` and `HarvestedNodeRestoreSystem` in `TerrainSystemGroup` (folder `Systems/TerrainSystemGroup/`). A cut tree becomes a stump (swap `ImageIndex`, clear `ResourceNode.remaining`); on tile load, nodes whose key is in the ledger and not yet due load as stumps; when due they regrow. The far view ignores cuts. The ledger must be saved with `WorldClockSnapshot`, unlike the spawn ledger.
- **WF-D12 Fields are draped meshes, not splat colours.** The palette has four ground layers and fields need many crop tints plus row direction. Each parcel is a mesh draped on the ground through the ribbon-road path (sub-asset of the tile data, `Painted/Field` shader reusing `PaintedLighting_float`), with tint and row direction per parcel. Spike WF-S2 confirms the far view draws them; fallback is a per-tile tint texture the ground shader samples.
- **WF-D13 Forest beyond the loaded ring is a baked canopy card batch per 1 km strategic super-chunk,** thinned (one clump card per several trees) to the platform budget. Without it the far view shows forest-coloured ground with no trees, which is the single biggest difference from the picture.
- **WF-D14 Rail reuses the road pipeline with its own profile:** carve (wider shoulders, stronger along-track smoothing, so embankments and cuttings appear), ribbon mesh (ballast, sleepers, rails in the texture), graph builder (`RailGraphSingleton`, same builder as the road graph). New `TerrainSurface.Rail`: walkable on foot, a wall for the caravan except inside a `LevelCrossing` crossing.
- **WF-D15 Editor UI follows the hard rule.** Scene view first: draw an outline by clicking, drag a box to stamp, drag knots to reshape, with a live fill preview before any bake. Panels second. Built to `EditorStyleGuide.md` R01 to R22 with before/after captures judged by the orchestrator. Synthetic events do not reach `duringSceneGui`, so mouse feel is always an owner checklist item.

## Skills Needed

`dots-authoring-baker` (station, harvest node authoring), `dots-system-scaffold` (ledger systems), `dots-test` (planner, subdivision, rail constraint, ledger fixtures), `shader-edit` (`Painted/Field`, card shader), `unity:ui-uitk` (Features panel).

## 3. Data model

Recipes are ScriptableObjects in `Data/Terrain/`; Features are MonoBehaviours in `MonoBehaviours/Terrain/` with a `SplineContainer`, raising a change event the baker debounces (copy `TerrainRoad` and `TerrainRoadEvents`).

| Type | Kind | Holds |
|---|---|---|
| `TerrainArea` | Feature, closed spline | `recipe` (any area recipe), `seed`, `priority`, `edgeNoiseMetres` |
| `TerrainLine` | Feature, open spline | `recipe` (hedgerow, tree line, fence, wall), `seed` |
| `TerrainRail` | Feature, open spline | `TerrainRailProfile`, priority |
| `TerrainStation` | Feature, point on a rail | name, platform prefab, side, links to the location registry |
| `TerrainStamp` | Feature, closed spline or box | `TerrainStampRecipe`, `seed`; children are generated |
| `TerrainGeneratedMarker` | on every generated object | `sourceFeatureId`, `locked` |
| `TerrainForestRecipe` | SO | `TerrainScatterSet`, ground layer to paint, density falloff toward the edge, clearing noise, interior-demotion distance |
| `TerrainFieldRecipe` | SO | crop styles (tint, row spacing, stubble/tall), border `TerrainLine` recipe, fallow chance |
| `TerrainVillageRecipe` | SO | building prefabs with lot sizes, street `TerrainRoadProfile`, lot depth, spawn marker templates, name list |
| `TerrainFarmlandRecipe` | SO, stamp | parcel size range, `TerrainFieldRecipe`, farmhouse prefabs, track profile |
| `TerrainRailProfile` | SO | gauge, bed width, `maxGradePercent`, `minCurveRadiusMetres`, ribbon style, viaduct pier prefab |
| `TerrainScatterRuleData` | existing, extended | `tier` (Dressing or Interactable), `cardAtlasEntry`, `areaEdgeBias` |

Per-tile bake products (new, beside the existing layers): `areaHeightDelta` (building pads only), `Tile_x_z_AreaMask.png` (which area owns each texel, read by the compositor and the scatter planner), `Cards_<atlas>` and `Field_<n>` mesh sub-assets of `TerrainTileData`, children `Cards`, `Fields`, `Buildings`.

## 4. How each thing in the picture is made

- **Forest:** draw a `TerrainArea` with a forest recipe. The baker rasterises the outline (with edge noise so it is ragged, not a polygon) into the area mask, the compositor paints the Forest layer under it, the planner fills it densely with the edge-biased rule and thins the interior to cards. Drag a knot: the tile re-bakes like a road edit.
- **Lone trees and tree lines:** `TerrainLine` with a tree-line recipe, or the existing scatter brush.
- **Farmland:** a `TerrainStamp` with a farmland recipe. A pure subdivider cuts the outline into parcels (split along the longest axis with jitter, never across a road, river or rail, which act as cut lines), each parcel becomes a `TerrainArea` with a field recipe and a random crop, parcel borders become hedgerow `TerrainLine`s, and one farmhouse goes on the flattest parcel corner nearest a road with a track joining it.
- **Village:** a `TerrainStamp` with a village recipe. It lays street `TerrainRoad`s from the nearest existing road into the outline, cuts lots along them, places a building prefab per lot on a flattened pad (`areaHeightDelta`), adds `NavFootprintAuthoring`, people as `TerrainSpawnMarkerAuthoring`, and one named location. Everything is a normal object under `Buildings` in its tile scene, so the owner drags a shop in, moves a marker, deletes a house; those edits lock (WF-D6).
- **Rail:** draw a `TerrainRail`. Segments steeper than `maxGradePercent` or tighter than `minCurveRadiusMetres` draw red in the Scene view while drawing. Where it meets a road the baker offers a `LevelCrossing`; where it meets a river or a drop deeper than the profile allows, a viaduct (deck mesh plus pier prefabs, a `Bridge` crossing underneath).
- **River, lake, road, bridge:** exist. They gain only the generated marker so stamps can create them.

## 5. Spikes (orchestrator runs these first; each ends with numbers in `Terrain_Spikes.md`)

- **WF-S1 Card look and cost.** One tile, 1,000 upright cards as (a) one batch mesh and (b) 1,000 baked entities, captured at mix 0 and mix 1 with `CameraWorldLean` on. Pass: trees stand straight at both zooms, shadows present and not culled (the lean culling trap in `Gotchas.md`), frame cost recorded for both. Decides the default interactable budget.
- **WF-S2 Field in the far view.** One draped parcel mesh on a test tile, then check whether the strategic stand-in shows it and whether road ribbons show today. Decides draped mesh versus tint texture.
- **WF-S3 Far forest.** One fully forested 1 km super-chunk as a thinned card batch; record triangles, draw calls and frame time against the Switch strategic budget (24 MB). Decides the thinning ratio.
- **WF-S4 Parcel subdivision.** Pure algorithm with an image dump of 20 seeds on irregular outlines with a road through them. The owner picks the look; this is the one place taste decides the algorithm.

## 6. Build phases (each sized for one or two `worker` briefs; gate with `devloop.py gate` per wave)

| Phase | Builds | Done when |
|---|---|---|
| WF-1 Feature core | `TerrainArea`, `TerrainGeneratedMarker` with auto-lock on edit, `TerrainFeatureBaker` (debounce, open tiles, stale list), pure `TerrainAreaRasterizer` with edge noise, area mask in the compositor, planner reads the mask | a drawn outline paints Forest ground and re-bakes on knot drag; rasteriser and lock tests fail when reverted |
| WF-2 Forest | `TerrainForestRecipe`, scatter `tier`, card-batch mesh builder (pure) and baker, card shader, first tree card atlas (placeholder art) | a forest area fills with trees, edge interactable and interior cards, budget line in the Tiles panel |
| WF-3 Cut and respawn | ledger buffer, record and restore systems, stump state, save hook | PlayMode: cut, unload tile, reload shows stump; advance clock, tree is back |
| WF-4 Scene-view authoring | Features tab: draw outline, drag box, recipe picker with thumbnails, live fill preview, selected-feature card (R01 to R22, captures) | owner draws a forest by pointing at the ground; captures reviewed |
| WF-5 Lines and fields | `TerrainLine`, `TerrainFieldRecipe`, draped field mesh builder (reuses the ribbon builder's draping), `Painted/Field` | a hand-drawn field shows rows and a hedgerow border at both zooms |
| WF-6 Farmland stamp | `TerrainStamp`, `TerrainParcelSubdivider` (pure), farmland recipe, Regenerate and Detach | drag a box, get a patchwork; move one hedge, regenerate, it stays |
| WF-7 Village stamp | village recipe, street and lot layout (pure), pads, footprints, spawn markers, named location | drag a box by a road, get a walkable hamlet with people; a hand-placed shop survives regenerate |
| WF-8 Rail track | `TerrainRail`, profile, constraint check, carve, ribbon style, `TerrainSurface.Rail` in the nav rules, level crossing, viaduct, `RailGraphSingleton`, `TerrainStation` | caravan is stopped by track and crosses at the crossing; graph route test between two stations |
| WF-9 Far forest | canopy batch in the strategic bake (after WF-S3) | zoomed out, forests have trees to the horizon within budget |
| WF-10 World sketch | `TerrainWorldSketcher` (pure): rivers downhill from the height field, village sites on flat ground near water, roads between villages by slope cost, one rail line under its grade limit, farmland around villages, forest on the rest; shows a flat overlay preview, Apply creates stamps and lines only | one click fills the test world; every result is selectable and movable |
| WF-11 Trains | separate spec, section 8 | |

Order reasoning: WF-1 to WF-4 put a drawable forest in the owner's hands first (largest visual gain, proves the whole pattern). WF-8 can run in parallel with WF-5 to WF-7 in its own worktree, since it touches the road pipeline rather than areas.

## 7. Shared files and the other terrain session

Another session owns the Terrain Editor window redesign (E3 to E5) and has uncommitted work on `TerrainTilesPanel`, `TerrainNavBaker`, `TerrainStreamingSystem` and `CaravanDriveSystem` in the `terrain-ease` worktree. This spec adds new files almost everywhere; the shared edits are: `TerrainCompositor` (area mask input), `TerrainScatterSet` and `TerrainScatterPlanner` (tier, area edge bias), `TerrainNavCellRules` and `TerrainSurface` (Rail), `TerrainRoadBaker` (rail shares the carve), the strategic baker (WF-9), and one new rail tab in `TerrainEditorWindow`. Agree the merge order with that session before WF-1 and build in a worktree.

## 8. Riding trains (needs its own spec, `TrainTravel_System.md`, and its own Q&A round)

What this spec guarantees for it: a baked `RailGraphSingleton`, named `TerrainStation`s in the location registry, level crossings, and track that streams. What the train spec must decide: timetable or on-demand departure; whether the train is one abstract traveller (`AbstractTravel` along the rail graph as a function of `WorldClock.totalWorldSeconds`, which is how caravans already move) that becomes real near the player; boarding moves `TerrainNavFocus` to the train so streaming and the nav window follow it, with the strategic view and time scale as in caravan mode; what happens to the player's caravan and minions when boarding; tickets and cost; events that stop a train. Entry pattern and group placement are decided there.

## 9. Verification (written out as `verify-world-features.md` when the build retires this spec)

Headless: rasteriser, subdivider, lot layout, rail constraint, card batch builder and ledger fixtures; seam check and budget report on a tile holding each feature; pixel-diff capture of real tiles against the strategic stand-in with fields and forest present. Owner only: drawing feel in the Scene view, parcel and village look, tree card look at both zooms, cutting a tree and seeing it return, driving the caravan over a level crossing.

## Open decisions

- [ ] **WF-Q1** Interactable tree budget per tile on PC, Deck and Switch ← DECISION after WF-S1 (default: 600, 300, 150).
- [ ] **WF-Q2** Tree respawn time ← DECISION (default: 3 game days, stump visible meanwhile, per recipe).
- [ ] **WF-Q3** Can units on foot walk through dense forest interior, or is it slow ground? ← DECISION (default: walkable, heavy nav cost, trunks are not walls).
- [ ] **WF-Q4** Do fields do anything (harvestable crops, trade goods) or are they scenery for now? ← DECISION (default: scenery; the area keeps its crop id so a later system can read it).
- [ ] **WF-Q5** Parcel look, chosen from the WF-S4 image sheet ← DECISION.
- [ ] **WF-Q6** Does track block units on foot anywhere (fenced lines)? ← DECISION (default: no).
- [ ] **WF-Q7** Train spec questions in section 8.
