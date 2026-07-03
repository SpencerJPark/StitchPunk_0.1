# Crowd-Scale Awareness Pass — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #13 — deferred from the May audit; do before the 200+-NPC crowd scene.

---

**Skills Needed:**
- `dots-system-scaffold` — only if the hash rebuild becomes its own system rather than extending `InteractionSpatialHashSystem` (§2)

---

## 1. Purpose & v1 scope

`EnemyAwarenessSystem` and `SocialAwarenessSystem` still scan the **faction multimap** (every unit of a faction, world-wide) while `ItemAwarenessSystem` got the spatial-cell upgrade. Fine at dozens of units; O(units × faction-size) at a 200+ crowd. Extend `SpatialHashRegistry` with unit cells and convert both systems to cell queries — **the `ItemAwarenessSystem` conversion is the template.** Then run the first real profile.

**v1 handles:** unit spatial cells, Enemy + Social awareness conversion, one profiling session with findings written to the vault.
**Out of v1:** LOD-ing awareness frequency (tick every N frames by distance-to-camera) — reserve as the follow-up if the profile says so.

## 2. Architecture

`SpatialHashRegistry` (singleton, `GameManagerSystemGroup`) gains `unitCells : NativeParallelMultiHashMap<int2, Entity>` alongside the existing `waypointCells` (+ item cells). Rebuilt where the current hashes are rebuilt (`InteractionSpatialHashSystem`) — same cell size, same rebuild cadence. Awareness systems replace the faction-multimap iteration with a 3×3-cell gather around the unit, then apply the exact same faction/alive/range filters they use today (the *filters* don't change; only the candidate set shrinks).

**← DECISION:** one combined `unitCells` map with per-entity faction lookup at query time (simple, one map) vs per-faction maps (fewer post-filter rejections, more rebuild cost). *Recommendation: one map — the faction filter is a cheap lookup the systems already do, and the crowd is mostly one faction anyway.*

## 5. Systems

- **Edited:** `GameManagerSystemGroup/InteractionSpatialHashSystem.cs` — add unit-cell rebuild (query: alive units with `LocalTransform`).
- **Edited:** `UtilityAwarenessSystemGroup/EnemyAwarenessSystem.cs`, `SocialAwarenessSystem.cs` — candidate gathering swaps to cells; scoring/filtering untouched (characterization: same targets chosen in a small scene before/after — that's the regression test).
- **Also while profiling (from the audit):** eyeball `BodyPart` at `[InternalBufferCapacity(32)]` = 512B in-chunk per character in the archetype/chunk-utilization view; write findings to `_Vault/Memories/Code/` (new `Performance.md` note).

## 8. Proposed file manifest

**Edited:** `SpatialHashRegistry` component, `InteractionSpatialHashSystem.cs`, `EnemyAwarenessSystem.cs`, `SocialAwarenessSystem.cs`
**New:** `_Vault/Memories/Code/Performance.md` (profile findings)

## 9. Build phases

1. `unitCells` rebuild + registry field (no consumers yet — inert).
2. `EnemyAwarenessSystem` conversion; small-scene A/B: identical target selection.
3. `SocialAwarenessSystem` conversion; same A/B.
4. Profile session: 200-unit crowd scene, Unity Profiler + Entities Debugger (chunk utilization, `BodyPart` buffer width, awareness job times before/after). Findings → `Performance.md`.

## 10. Verification

Correctness: A/B scene (10 units) — same threat targets, same talk partners pre/post conversion. Scale: 200-unit spawn — awareness job time drops from O(n·faction) to flat-ish; frame time recorded before/after in `Performance.md`. Watch the rebuild cost doesn't just move the spike (hash rebuild is per-frame over all units — acceptable, it's O(n) with tiny constant).

## Open decisions (collected)

- [ ] §2 — single unitCells map (recommended) vs per-faction maps.
- [ ] §9.4 — profile-driven follow-ups (awareness LOD, buffer capacity change) get their own plan vs amend this one.
