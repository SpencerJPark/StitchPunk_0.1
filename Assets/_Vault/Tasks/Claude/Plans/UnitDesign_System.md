# Unit Design System — Design Spec

> **Status:** 🔨 built — code landed (components, authoring, randomize/apply/change systems, `DesignSystemGroup`). Editor wiring + play-test pending → see [`../../Spencer/verify-unit-design-system.md`](../../Spencer/verify-unit-design-system.md).
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "SaveSystem" / "add random unit designs"

## Context

Save's next steps depend on minions having **persistent, unique visual identity**. Today a unit's look is driven by per-part `ImageIndex` values authored statically on each body-part quad (the same index the animation flipbook drives). There's no way to (a) randomize a unit's appearance on spawn from a *valid* slice of the texture array, or (b) persist that appearance so a reanimated minion keeps its identity across saves.

This spec rebuilds the **unit-design layer only**: a `DesignAuthoring` on the main body that declares, per part, one or more **valid index ranges** into the texture array (so randomization picks human parts, never zombie parts), a spawn-time system that rolls a random index per part, and persistence of those indices for minions via the existing `IPersist` save pipeline. Control cleanup is explicitly **out of scope** for this pass.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../../Memories/Code/Skills.md)):
- `dots-authoring-baker` — `DesignAuthoring` + Baker, flattening per-part range entries into two buffers (§4, §8)
- `dots-system-scaffold` — `DesignRandomizeSystem` and `DesignApplySystem` (ISystem + IJobEntity) (§5)

*(No `dots-blob-library` — config is small per-instance buffers, not an SO→Blob library. No `dots-unit-ai` — this is visual, not behavioural.)*

---

## 1. Purpose & v1 scope

On spawn, pick a random valid texture-array index for each customizable body part and apply it to that part's quad; for units that are minions, persist those indices so they restore identically. Entry is the **component-on-entity request model**: `DesignAuthoring` bakes the per-part ranges + a `RandomizeDesign` enableable; a spawn-init system consumes the request, rolls indices, and disables it.

**v1 handles:**
- `DesignAuthoring` on the main body declaring, per `AnimationTarget` part, a list of valid `[min,max]` index ranges (multiple ranges per part → union of valid slices, excluding zombie parts).
- Random index selection per part on first spawn (request-gated; pooled/reclaimed units keep their look).
- Applying chosen indices to the correct child quads by resolving them through the already-rebuilt `AnimatorTarget` buffer.
- Persisting chosen indices on minions via a blittable `IPersist` component — restored identically by the existing `MinionRestoreApplySystem`.

**Out of v1:**
- Control/input cleanup (called out by the user as a separate follow-up).
- Human→zombie conversion **re-rolling** into a *zombie* range set — the per-part range buffers stay on the entity, so a future conversion system can re-randomize against a different range; **reserved hook**, not built here. *(The explicit-index runtime path — set named parts to exact indices, e.g. zombie skin — is now specified in §5b `ChangeDesignRequest`; only the re-roll-against-a-zombie-range variant stays reserved.)*
- Color/shape enum design (`UnitSkinColor`, `UnitHairColor`, `UnitHeadShape`, `RandomizeDesign` already exist in `UnitDesignComponents.cs`) — this pass reuses the existing `RandomizeDesign` flag but drives texture indices, not the color enums. ← DECISION: leave the color-enum components in place untouched, or fold them into the index model later.

## 2. Architecture

Pure ECS — no MonoBehaviour bridge. Runs entirely in the existing **`SpawnInitSystemGroup`** (under `LateSimulationSystemGroup`), the established home for one-shot per-spawn initialization that must run *after* `AnimatorTargetInitSystem` has rebuilt the body-part buffer.

**Key reuse:** rather than baking literal child-entity references (the ragdoll `Ragdoll2DJointRef` pattern, which inherits the "entity refs in buffers aren't remapped by `ECB.Instantiate`" gotcha), the design data is keyed by the `AnimationTarget` **enum**. The child quad for each part is resolved at runtime through the root's `AnimatorTarget` buffer, which `AnimatorTargetInitSystem` already rebuilds from `BaseParent` (an `IComponentData`, correctly remapped). No custom remap code, no `PostBakingSystemGroup` system.

The range data still mirrors the ragdoll **joint→zones flat-buffer** shape (`Ragdoll2DJointRef.zoneStart/zoneCount` → `Ragdoll2DJointZone`): a part entry holds a `rangeStart/rangeCount` window into a flat range buffer.

```
SpawnInitSystemGroup (LateSimulationSystemGroup), per NewlySpawned root:
  SpawnStateInitSystem ─▶ AnimatorTargetInitSystem ─▶ DesignRandomizeSystem(NEW)
        ─▶ MinionRestoreApplySystem ─▶ DesignApplySystem(NEW) ─▶ SpawnInitCleanupSystem
  (roll)                       (restore overwrites roll)      (fan indices → child quads)
```

Randomize writes the *chosen indices into a component*; restore (for saved minions) overwrites that component from disk; **apply** then fans whichever indices won out to the child quads. Restore wins automatically because it runs between roll and apply.

## 3. Entry points

- **Request (one-shot, on entity):** `RandomizeDesign : IComponentData, IEnableableComponent` — **already exists** in `UnitDesignComponents.cs`. `DesignAuthoring` bakes it **enabled**. `DesignRandomizeSystem` reads it on `NewlySpawned` roots, rolls indices, then disables it. Pooled/reclaimed entities have it already disabled → keep their prior design. Restored minions: a wasted roll may run, but restore overwrites the component before apply.
- **Config (persistent, on entity):** `DesignPart` + `DesignRange` buffers (baked once, read every randomize) — see §4.
- **Result (persistent, on entity):** `PersistedDesign : IComponentData, IPersist` — the chosen indices; snapshotted generically by the save pipeline (§4).
- **Runtime re-skin (one-shot, on entity):** `ChangeDesignRequest : IComponentData, IEnableableComponent` — a **batch** of explicit `(AnimationTarget, imageIndex)` changes; baked present + disabled, enabled by any caller (first caller: zombie conversion), consumed and disabled by `DesignChangeSystem` (§5b). Distinct from `RandomizeDesign` (spawn-time random roll) — this sets **exact** indices at any time and upserts them into `PersistedDesign` so the new look persists.

## 4. Data model

All per-instance buffers/components on the **root body entity** — no SO→Blob library.

```csharp
// Config — baked by DesignAuthoring, mirrors Ragdoll2DJointRef/Zone.
[InternalBufferCapacity(16)]
public struct DesignPart : IBufferElementData
{
    public AnimationTarget target;  // which body part this entry customizes
    public int rangeStart;          // window into the DesignRange buffer
    public int rangeCount;
}

[InternalBufferCapacity(32)]
public struct DesignRange : IBufferElementData
{
    public int min;                 // inclusive valid texture-array index
    public int max;                 // inclusive
}

// Result + persistence — blittable, so the existing IComponentData serializer
// snapshots/restores it with ZERO changes to SaveSerialization/PersistRegistry.
public struct PersistedDesign : IComponentData, IPersist
{
    public FixedList512Bytes<DesignSlot> slots;   // ~62 slots cap ≫ ~32 AnimationTarget parts
}

public struct DesignSlot   // unmanaged: blittable inside FixedList
{
    public int target;      // (int)AnimationTarget
    public int imageIndex;  // chosen slice
}

// Runtime re-skin request — caller fills `changes`, enables the component;
// DesignChangeSystem upserts into PersistedDesign + applies to quads, then disables it.
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public FixedList512Bytes<DesignSlot> changes;  // reuse DesignSlot; explicit absolute indices
}
```

**Authoring inspector shape** (mirrors `Ragdoll2DAuthoring.joints` → `Ragdoll2DJointEntry.landingZones`):
```csharp
[Serializable] public class DesignPartEntry { public AnimationTarget target; public List<IndexRange> ranges; }
[Serializable] public class IndexRange { public int min; public int max; }
```
The Baker flattens `List<DesignPartEntry>` into the `DesignPart` + `DesignRange` buffers (assign `rangeStart`/`rangeCount` as it appends), exactly like `Ragdoll2DAuthoring.Baker` builds `jointBuffer` + `zoneBuffer`.

**Persistence note:** `PersistRegistry` auto-discovers any value-type `IPersist` `IComponentData` and excludes types containing `Entity`/`BlobAssetReference` fields. `PersistedDesign` (FixedList of ints, no Entity) qualifies — verify it isn't filtered (§10). The save query already covers these units: `WithAll<Minion, UnitData, LocalTransform>.WithDisabled<Dead>()` in `PersistentSaveSystem`.

## 5. Systems

Both new systems live in `Assets/_Scripts/Systems/LateSimulationSystemGroup/SpawnInitSystemGroup/`, `[UpdateInGroup(typeof(SpawnInitSystemGroup))]`, `RequireForUpdate<GameSceneTag>`, filter on `NewlySpawned`.

**`DesignRandomizeSystem`** — `[UpdateAfter(typeof(AnimatorTargetInitSystem))]`, `[UpdateBefore(typeof(MinionRestoreApplySystem))]`.
- Query: `NewlySpawned` roots with `RandomizeDesign` **enabled** + `DesignPart`/`DesignRange` buffers + `PersistedDesign`.
- For each `DesignPart`: pick a random `[min,max]` from its range window (uniform over the union of values — weight range choice by `(max-min+1)` so every valid slice is equally likely), roll `imageIndex`, append `DesignSlot{target,imageIndex}` to `PersistedDesign.slots` (clear first).
- Disable `RandomizeDesign` (one-shot consume).
- Seed `Unity.Mathematics.Random` from `(uint)(SystemAPI.Time.ElapsedTime * k) ^ entityIndexInQuery` — never seed 0. `IJobEntity` + `.ScheduleParallel()` (no cross-entity writes, all data on the root). Burst-compatible.

**`DesignApplySystem`** — `[UpdateAfter(typeof(MinionRestoreApplySystem))]` (so restored indices win), before `SpawnInitCleanupSystem`.
- Query: `NewlySpawned` roots with `PersistedDesign` + `AnimatorTarget` buffer.
- For each `DesignSlot`, find the matching `AnimatorTarget` by `target` enum → child quad entity. Write to that child via `ComponentLookup`: `AnimationTargetRestPose.baseImageIndex = imageIndex` (canonical no-animation source) **and** `ImageIndex.index = imageIndex; ImageIndex.onUpdate = true` (so `UpdateImageIndexSystem` pushes `_ImageIndex` to the material next frame). Skip parts absent from `AnimatorTarget` (e.g. `AnimationTargetNoIndexAuthoring` parts).
- Writes target child entities via `ComponentLookup<…>` (parallel-write disabled or `.Schedule()` single-thread worker — spawn-init volume is low; **never `.Run()`**). Reads `AnimationTarget` enum from `AnimationComponents.cs`.

*Restore needs no new code:* `MinionRestoreApplySystem` already calls `SaveSerialization.ApplyEntity`, which generically restores `PersistedDesign` (an `IPersist` component) onto the respawned minion before `DesignApplySystem` runs.

## 5b. Runtime re-skin — `ChangeDesignRequest` / `DesignChangeSystem`

A generic primitive to **change the int (image index) of specific body parts at runtime**, distinct from the spawn-time random roll. First caller is human→zombie conversion (skin parts flip to zombie slices), but it is reusable (debug re-skin, future cosmetic swaps). Decisions locked: **batch** payload, **explicit absolute** indices, **persisted** via `PersistedDesign`.

**Why a root component (not separate request entities, not per-child):** every request in this codebase (`HealRequest`, `AttackRequest`, `PathRequest`, `SwapBrainRequest`) is an enableable component on the *target unit*, consumed in place — a standalone request-entity would be the lone exception with no payoff for a single batched skin swap. And `PersistedDesign` lives on the **root**, so the root upserts its own persistence locally and resolves children through the `AnimatorTarget` buffer (already rebuilt from `BaseParent` by `AnimatorTargetInitSystem`) — no entity refs to remap, no per-child fan-out by the caller.

**`ChangeDesignRequest`** baking: `DesignAuthoring.Baker` (§8) adds it present and `SetComponentEnabled(false)` — same way `UnitBakingUtil.BakeRequirements` bakes `SwapBrainRequest` disabled. Units without `DesignAuthoring` simply can't receive it.

**`DesignChangeSystem`** (NEW) — `[BurstCompile]`, `RequireForUpdate<GameSceneTag>`. **Not** gated on `NewlySpawned` and **not** in `SpawnInitSystemGroup` (the request fires any frame, e.g. mid-game conversion). Runs **after** the conversion trigger (`HealthSystemGroup` — `ReviveRequestSystem`/`SwapBrainSystem`) and **before** `AnimationSystemGroup`'s `UpdateImageIndexSystem` so the material reflects the change promptly (one-frame delay is harmless). ✅ RESOLVED: lives in a dedicated `DesignSystemGroup` declared in `SystemGroups.cs` (`[UpdateAfter(HealthSystemGroup)] [UpdateBefore(AnimationSystemGroup)]`).
- Query: roots with `ChangeDesignRequest` **enabled** + `PersistedDesign` + `AnimatorTarget` buffer.
- Per `DesignSlot{target, imageIndex}` in `changes`:
  - **Upsert** `PersistedDesign.slots` — overwrite the matching `target`'s `imageIndex`, or append if absent. Keeps `PersistedDesign` authoritative so the save pipeline captures the new look (zero save-code changes — same generic `IPersist` path as §4).
  - **Apply** via the shared helper (below): resolve `target` → child quad through `AnimatorTarget`, write `AnimationTargetRestPose.baseImageIndex = imageIndex` **and** `ImageIndex.index = imageIndex; ImageIndex.onUpdate = true`. Skip parts absent from `AnimatorTarget`.
- Disable `ChangeDesignRequest` (one-shot consume).
- **Scheduling:** writes child entities via `ComponentLookup<ImageIndex>` / `ComponentLookup<AnimationTargetRestPose>` → `.Schedule()` (single-thread worker, no parallel write hazard on children); **never `.Run()`**.

**Shared apply helper:** the per-slot "resolve via `AnimatorTarget` + write `baseImageIndex`/`ImageIndex`" logic is identical to `DesignApplySystem` (§5). Extract it into a static `DesignApplyUtil.ApplySlot(...)` and call it from **both** systems — no duplicated apply code.

**Zombie caller (integration, not built here):** `SwapBrainSystem` ([MinionRevival_System](MinionRevival_System.md)) fills `ChangeDesignRequest.changes` with the explicit zombie skin indices and enables it during conversion. Where those indices come from (a zombie skin set/config) is owned by the conversion spec; this spec delivers only the generic request + consumer.

## 7. Integration points

- **Animation (`AnimationComponents.cs`, `AnimationSamplingSystem`, `UpdateImageIndexSystem`):** design writes `AnimationTargetRestPose.baseImageIndex` + `ImageIndex.index`; sampling uses `baseImageIndex` as the per-frame fallback, so static cosmetic parts render the chosen slice. **Caution:** parts genuinely driven by a flipbook track will have `imageIndex` overwritten each frame by keyframes — design indices are meaningful for non-animated/cosmetic parts (head shape, hat, glasses, skin). Block-offset variants for animated parts are out of v1.
- **Spawn pipeline (`UnitSpawnerSystem`, `AnimatorTargetInitSystem`, `SpawnInitCleanupSystem`):** new systems slot into `SpawnInitSystemGroup`; rely on `AnimatorTarget` already rebuilt and `NewlySpawned` still set.
- **Save (`PersistentSaveSystem`, `PersistentLoadSystem`, `MinionRestoreApplySystem`, `PersistRegistry`, `SaveSerialization`):** zero code changes — `PersistedDesign` rides the generic `IPersist` path. Minion identity continues via existing `Minion` + `PersistId`.
- **Existing design components (`UnitDesignComponents.cs`):** reuse `RandomizeDesign`; leave color/shape enum components untouched (← DECISION above).
- **Unit baking (`UnitBakingUtil`, `MinionAuthoring`):** `DesignAuthoring` is a sibling authoring on the same body GameObject; no change to `UnitBakingUtil`. Ensure the citizen prefab carries `DesignAuthoring` with ranges filled.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Components/Units/DesignComponents.cs` — `DesignPart`, `DesignRange`, `PersistedDesign`, `DesignSlot`, **`ChangeDesignRequest`** (§5b).
- `Assets/_Scripts/Authoring/Units/DesignAuthoring.cs` — MonoBehaviour (`List<DesignPartEntry>`) + nested Baker (flattens to buffers, adds `RandomizeDesign` enabled + empty `PersistedDesign` + **`ChangeDesignRequest` disabled**). Models `Ragdoll2DAuthoring`.
- `Assets/_Scripts/Systems/LateSimulationSystemGroup/SpawnInitSystemGroup/DesignRandomizeSystem.cs`
- `Assets/_Scripts/Systems/LateSimulationSystemGroup/SpawnInitSystemGroup/DesignApplySystem.cs`
- `Assets/_Scripts/Systems/.../DesignChangeSystem.cs` — runtime re-skin consumer (§5b; group TBD, **not** `SpawnInitSystemGroup`).
- `DesignApplyUtil` (static helper, alongside the systems) — shared `ApplySlot(...)` used by `DesignApplySystem` + `DesignChangeSystem`.

**Edited:** none required (save pipeline untouched). Optionally extend `_Vault/Memories/Code/Components.md`, `Authoring.md`, `Systems.md` with the new types after build.

**Assets:** add `DesignAuthoring` to the citizen body prefab and author per-part valid ranges in the inspector (Editor-only, Spencer).

## 9. Build phases

1. **Data layer** — `DesignComponents.cs` (`dots-system-scaffold` conventions for the structs). Confirm `PersistedDesign` compiles and is picked up by `PersistRegistry`.
2. **Authoring** — `DesignAuthoring` + Baker (`dots-authoring-baker`); flatten entries to `DesignPart`/`DesignRange`, bake `RandomizeDesign` enabled + empty `PersistedDesign`. Mirror `Ragdoll2DAuthoring.Baker`.
3. **Randomize** — `DesignRandomizeSystem`; roll indices into `PersistedDesign`, disable `RandomizeDesign`.
4. **Apply** — `DesignApplySystem`; fan slots to child quads via `AnimatorTarget` (extract `DesignApplyUtil.ApplySlot`). Visible randomized look on spawn.
5. **Persist round-trip** — add `DesignAuthoring` to a minion, save, relaunch, load; confirm identical indices restored (no new save code).
6. **Runtime re-skin** — `ChangeDesignRequest` (baked disabled) + `DesignChangeSystem` (reuses `DesignApplyUtil.ApplySlot`); upsert `PersistedDesign` + apply. Fire from the Entities inspector → parts change to the exact indices and persist; later wired to `SwapBrainSystem` for zombie conversion.

## 10. Verification

Test in `DOTSTestScene`:
- **Phase 3–4:** add `DesignAuthoring` to the citizen prefab with a couple of parts (e.g. head, hat) and valid ranges. Enter Play; each spawned unit shows a randomized slice within range, never an out-of-range/zombie slice. Inspect `PersistedDesign.slots` in the Entities inspector — one `DesignSlot` per declared part, `imageIndex` inside its `[min,max]`. Spawn several units → visibly different looks. Reclaim a pooled unit (kill + respawn) → look is **unchanged** (`RandomizeDesign` stayed off).
- **Phase 5:** mark a unit a `Minion`, trigger a save (autosave or manual `SaveRequest`), confirm the slot JSON carries a `PersistedDesign` `ComponentRecord`. Relaunch, `LoadRequest` → the minion respawns with the **same** indices (compare inspector values pre/post). A pre-design save (no `PersistedDesign` record) → unit falls back to a fresh random roll (acceptable).
- **Phase 6 (runtime re-skin):** on a spawned unit, fill `ChangeDesignRequest.changes` (e.g. Body→X, Head→Y) and enable it in the Entities inspector → the quads change to those exact slices and the request flips back to disabled the same frame. Inspect `PersistedDesign.slots`: matching `target`s show the new `imageIndex` **upserted** (not duplicated), untouched parts unchanged. Save/reload a `Minion` after a change → it returns with the **changed** look (re-skin survived). A `change` for a part absent from `AnimatorTarget` is skipped cleanly. Once `MinionRevival_System` lands: revive a corpse → its skin parts flip to zombie indices via the fired request and persist.
- **Editor-only (Spencer):** authoring per-part ranges, confirming texture-array slice layout (which indices are human vs zombie per part), and that cosmetic vs flipbook-animated parts behave as expected.

## Open decisions (collected)
- [ ] §1 — leave existing color/shape enum design components (`UnitSkinColor` etc.) untouched, or migrate them into the index-range model.
- [ ] §1/§2 — reserve per-part zombie ranges now (extra range entries) for the future human→zombie conversion, or add them when that system is built.
- [ ] §5 — range-pick weighting: uniform across the union of all valid slices (recommended) vs uniform across range *entries*.
- [ ] §8 — confirm `FixedList512Bytes` is the right capacity (≤~62 slots) for the maximum designable part count, or drop to `FixedList128Bytes`.
- [x] §5b — `DesignChangeSystem` group placement: **resolved** — dedicated `DesignSystemGroup` (after `HealthSystemGroup`, before `AnimationSystemGroup`).
- [ ] §5b — source of the explicit zombie skin indices the conversion caller writes into `ChangeDesignRequest` (a zombie skin set/config) — owned by [MinionRevival_System](MinionRevival_System.md), confirm when that build reaches the conversion-wiring phase.
