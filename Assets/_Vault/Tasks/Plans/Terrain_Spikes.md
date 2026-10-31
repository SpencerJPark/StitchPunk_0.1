---
tags: [plan, terrain, spikes]
related: "[[Terrain_System]]"
---

# Terrain spikes: findings

Run through the Unity MCP, 2026-10-30. Each finding is the answer plus the TT-D it confirms or changes.

## S1 Height collider and unit gravity: PASS
- `UnitGravitySystem` is one `CastRay` (start 0.5 m above the feet, filter `CollidesWith = groundLayerMask`) against the collision world, so any static collider on the Ground layer works.
- Probed a 101 x 101 hill (5 m, sigma 15 m) in a hand-built `PhysicsWorld`: `TerrainCollider` and `MeshCollider` both hit 2000/2000 random rays, worst height error 0.005 m, and a ray masked to another layer misses.
- Blob size: heightfield 76 KB, mesh 807 KB. Raycast: heightfield about 1.6x faster. (`RigidBody.Scale` must be 1 in a hand-built world or nothing is hit.)
- Baking: `PhysicsShape` only builds a Terrain collider from a Unity `TerrainData`, which TT-D15 rules out. **Decision: P2-4 uses a legacy `MeshCollider` (bakes with no extra code); a custom baker that builds `TerrainCollider.Create` directly is a later size optimisation (807 KB x 9 loaded tiles = 7 MB, inside TT-D28).** TT-D "heightfield if the spike confirms" is confirmed as possible, deferred.

## S5 SDF encode speed: dirty-rect encode is required, Burst is not
- Full managed `EncodeSignedDistanceField`: **62 ms at 256 px, 236 ms at 512 px** (3 channels). Neither holds 30 fps, so P3-1's dirty-rect encode is mandatory.
- A brush stamp of radius 5 m dirties about (2 x (5 + 4 m) / 0.39 m)^2 = 46^2 texels at 256 px, about 1/30 of the tile, so the managed rect encode lands near 2 ms. Write it managed; revisit Burst only if measured above 8 ms.

## S6 Painted shader on a slope and cliff: PASS
- Built a 30 degree ramp (8 m rise) into a near-vertical 6 m face on the tile material and captured it in the Scene view.
- The 30 degree ramp keeps the painted look (world-projected, mild stretch), the face switches to the cliff texture with vertical streaks. Cliff blend is `normal.y` 0.45 to 0.70 (about 46 to 63 degrees), so ramps up to 45 degrees stay painted.
- Decals on slopes were not tested here; they come back in P6 (S3).

## S7 Unity Splines 2.8.4 API: ready, no surprises
- Package `com.unity.splines@2.8.4`, assembly `Unity.Splines` (Cinemachine already references it; `StitchPunk.MonoBehaviours` must add it for P5).
- Runtime: `SplineContainer` (MonoBehaviour), `Spline.Changed` (static `Action<Spline,int,SplineModification>`) and instance `Spline.changed`, `SplineContainer.SplineAdded/Removed`, `SplineUtility.EvaluatePosition/EvaluateTangent/CalculateLength`, `SplineUtility.GetNearestPoint(spline, float3 point, out nearest, out t, resolution, iterations)` plus a `Ray` overload.
- Editor: `SplineToolContext` (`[EditorToolContext("Spline", typeof(ISplineContainer))]`); tools register with `editorToolContext: typeof(SplineToolContext)`.

## S11 Real data size per tile
- SDF PNG on disk: **42 KB at 256 px, 147 KB at 512 px**; hard mask PNG 6 KB / 16 KB (60 random stamps); RGBA32 in memory 256 KB / 1 MB.
- Compression, measured as round-trip error in SDF levels (1 level = 0.031 m): **DXT1/DXT5 max error 41 levels = 1.3 m at 256 px, unusable** (visible stepping at borders). **BC7 max 10 levels = 0.3 m at 256 px and 7 levels at 512 px, mean 0.5 levels**, memory 64 KB at 256 px, 256 KB at 512 px.
- **Decision (TT-D8/D28): the SDF import setting is BC7 (`TextureImporterFormat.BC7`), mipmaps off, set in P3-0.** 256 px tile costs 64 KB resident. Height 4 arrays x 40 KB = 160 KB, tile mesh about 450 KB, so a wilderness tile lands near 0.7 MB, inside the 1.2 MB budget.
- Not measured: PNG import time and the mesh collider baked size (S1 gives 807 KB).

## S5 follow-up (P3 built)
- Live paint on tile (0,0) at 512 px, measured through the real brush events: first stamp 215 ms (session creation), later stamps about 15 ms each (region encode plus the whole-texture upload), stroke end 860 ms (PNG + full composite + BC7 reimport). Holds 30 fps; a 1024 px hero tile would need a partial texture upload.

## S3 Decals bake to entities with per-instance tile and tint: PASS once PAINTED_ENTITIES is defined
- The define was not set. Without it the baked decals have no `_Tile`/`_Tint` entity components, and Entities Graphics ignores `MaterialPropertyBlock`, so every decal in a tile subscene would draw atlas tile 0.
- `PaintedGround/Editor/PaintedEntitiesDefine.cs` (`[InitializeOnLoad]`) adds `PAINTED_ENTITIES` to the Standalone defines when Entities Graphics is present; `PaintedDecalEntities.cs` then compiles into Assembly-CSharp. Adding it recompiles everything and the Editor stalled for about 10 minutes the first time (MCP link lost until the owner restarted it): expect that once on a fresh checkout.
- Probe: 16 decals (atlas tiles 0-7, opacity 0.5-0.8) in a tile scene gave 16 entities with `PaintedDecalTile` 0..7 (two each) and tint alpha 0.50..0.80 in Play. Rendering of the instanced `_Tile` was not looked at (owner check).
- TT-D11 stands: decals bake to entities. The 500 legacy decals in `CampLoop` stay GameObjects in the main scene (they are not in a subscene).

## S10 Per-surface unit speed: PASS, transient scale around the mover (2026-10-31)
- `UnitMoverSystem` reads `Movement.moveSpeed` / `runSpeed` each frame (`MovementComponents.cs:6-14`, `UnitMoverSystem.cs:70`). No multiplier component exists in the toolkit or the game.
- Other writers of `moveSpeed`: `DrunkStumbleSystem` (every frame, drunk units), `PlayerRollMoveSystem` (derived distance, restored at roll end), `IntoxicationDecaySystem`; `BehaviorExecutionSystem.cs:246` and `PlayerRollStartSystem.cs:87` CAPTURE the current `moveSpeed` as a base, so a persistent scale would be saved back as the base.
- **Decision for P8-6:** a transient `SurfaceSpeedFactor { factor, appliedFactor }`. An apply system multiplies `moveSpeed` and `runSpeed` before `MovementExecutionSystemGroup` (after `DrunkStumbleSystem`), a revert system divides them back after `UnitMoverSystem`. Skip entities with `PlayerRoll`. No package edit. Open: the order between the follower group and the execution group was not verified; confirm when building. The caravan does not use `Movement` (`CaravanDriveSystem.cs:93` reads its profile), so surface speed for the caravan is a separate seam.

## S9 Lighting: no day/night or lightning code exists (2026-10-31)
- The only clock is the ECS `WorldClock.timeOfDay01` (`WorldClockSystem.cs:52-57`); its readers are trade, save/load and HUD. Nothing rotates a sun or tints ambient; no `Shader.SetGlobal*` and no lightning code anywhere. `RenderSettings` is written only by the editor-only `SampleIslandBuilder`. Scene lights in `Game.unity` / `CampLoop.unity` were not opened.
- Ground lighting is `PaintedLighting_float` (`PaintedGroundCore.hlsl:123-166`): `SampleSH(float3(0,1,0)) * _AmbientTint`, `GetMainLight` with flat-ish N.L, point lights with a soft shoulder and `pointBoost`. `PaintedCard`, `PaintedDecal`, `PaintedFog` and `PaintedWater` each carry their own copy of the SH + main light code.
- **Consequence for TT-D29 / P10-8:** there is no existing driver to hook, so `TerrainLightingBridge` must also BE the day/night driver (read `WorldClock` via the ECS world like `WorldClockHudUI.cs:44`, set globals in `LateUpdate`/`OnEnable`). Declare the new globals in HLSL only (not in a `CBUFFER`/DOTS property block, which breaks SRP Batcher), default them to a neutral value so an unset global does not black out ambient, and update all five lighting copies. Edit only the `Assets/` copy of `PaintedGroundCore.hlsl`, not `.claude/worktrees/`.
- Alternative needing no shader edit: drive `RenderSettings.ambientLight` and the sun `Light` from the bridge, but then lightning cannot flash the terrain independently of other objects.

## S8 Unit intent to abstract route: gaps found (2026-10-31)
- There is no per-unit schedule or trade-run route component. `ThinkScheduleSystem` is a think-rate throttle; `WorldClockComponents.cs:3` mentions Schedules but they were never built. Trade components are order and letter data; `Caravan` is the player's drivable workstation.
- Mapping (live intent -> abstract anchor): `StateMachine.hasTargetPosition` -> `targetPosition`, else the target entity's position (`UtilityAiComponents.cs:31-39`); Wander -> rest anchor (spawn or last waypoint); minion move order -> `targetPosition`; follow / return to player -> Player position, `PinnedActive`; Melee / Projectile / Flee -> not abstractable (materialise or drop the intent); Talk, Sit, Smoke, Drink, Eat, Sleep, Bathroom, Interact -> local, skipped while abstract; Patrol / GuardPost -> the `GuardPost` position; traders and caravans -> abstract from birth via `AbstractTravel.BeginRoute` and `RoadGraphQuery.TryFindRoute` (`Terrain_System.md:133`).
- `PinnedActive`: Player, `Minion` / `PlayerUnitBrain` units, the player's caravan while driven or parked, any unit with `CutsceneActor` enabled. Abstractable: persistent camp and town units, NPC traders, and non-persistent wildlife and enemies (which just despawn on unload).
- Not resolved: `UnitSO` home/work/goal fields (greps found none), whether a trader NPC entity with a destination exists, whether the `AbstractRoute` struct exists yet. Check at P9-4.

## S2 Tile subscene load by coordinate: API confirmed, hitch NOT measured (2026-10-31)
- Entities 6.5 `SceneSystem` (`Unity.Scenes/SceneSystem.cs`): `LoadSceneAsync(WorldUnmanaged, EntitySceneReference, LoadParameters)` (:381) returns the scene entity, `LoadParameters.AutoLoad` / `Flags` / `Priority`, `UnloadScene(world, sceneEntity, UnloadParameters)` (:503; the default keeps the meta entities so a reload is faster, `DestroyMetaEntities` fully unloads), `IsSceneLoaded(world, entity)` (:167), `GetSceneEntity(world, Hash128)` (:609). So P9-1 can store an `EntitySceneReference` per tile in the buffer. The repo has no existing `SceneSystem` or `EntitySceneReference` use.
- Open: the load/unload hitch for a real tile (a measurement in Play), and whether `EntitySceneReference` bakes from a SubScene child on the `TerrainWorld` object.

## S4 Nav cost on a large grid: NOT run
- Needs a profiled Play run with a temporarily enlarged `NavGridAuthoring` (`Packages/com.dotsmovementtoolkit`). Still open.
