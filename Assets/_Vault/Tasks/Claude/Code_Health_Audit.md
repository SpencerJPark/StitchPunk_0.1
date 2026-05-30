---
tags: [task, claude, code, audit, scalability]
related: "[[Memories/Code/Systems]], [[Memories/Code/Systems_AI]], [[Memories/Code/RULES]], [[Tasks/Claude/Code_Systems]]"
created: 2026-05-29
status: active
---

# Code — Scalability & Maintenance Audit

Snapshot of growth-curve issues identified during the throw-item refactor session (2026-05-29). None of these are broken today — they're the patterns that bend wrong if not addressed early. Ordered by severity-as-content-scales.

**Health goal:** keep things simple, decoupled, scalable, and transferable. The blob library + SO authoring pipeline is the single biggest transferability win — protect it.

---

## Healthy patterns (preserve)

- [[Memories/Code/Systems|System group hierarchy]] in `Assets/_Scripts/Systems/SystemGroups.cs` — clean ordering, readable in 30 seconds.
- **Blob library pattern** consistent across Item/Attack/Effect/Animation/Unit/Factory/Scoring/Interaction. Transferable convention.
- **Request/Action decoupling** — input → request → executor → downstream. Combat → Movement → Animation talks via components, not method calls. Throw refactor (this session) is a reference example.

---

## Watch as content scales

### 1. `ActionType` + per-action tag explosion
- **Where:** `Assets/_Scripts/Data/Enums/AiEnums.cs` (25+ `ActionType` values), `Assets/_Scripts/Components/AI/AiComponents.cs:77–88` (11 enableable action tags).
- **Problem:** Each new content action = 1 enum value + 1 tag component + 1 ExecutionSystem. Fine at 10 actions, painful at 50.
- **Options:**
  - [ ] Stay tag-per-action but extract a shared "action orchestration" helper for the common pattern (path-to-target → animate → fire request → complete) so a new `EatAction` is ~30 lines instead of ~300.
  - [ ] Or: collapse to `ActiveActionRequest { ActionType type, Entity target }` + function-pointer dispatch (pattern already exists in `SelectionFunctions`). Trades discoverability for compactness — overkill until 30+ actions.
- [x] Extracted dual-registration sync hazard: moved the 14 `ActionType → function-pointer` registrations from both `ActionSelectionSystem.OnCreate` and `ActionInterruptSystem.OnCreate` into `SelectionFunctions.PopulateFunctionTable`. Adding a new implemented ActionType now requires touching `SelectionFunctions.cs` only. (Done 2026-05-29.)

### 2. `MotivationType` mixes needs and traits
- **Where:** `Assets/_Scripts/Data/Enums/AiEnums.cs:1–25`.
- **Problem:** `Hunger`/`Energy`/`Bladder` are needs (decay, want satisfaction). `Bookworm`/`NightOwl`/`Lazy`/`Slob`/`Grumpy` are personality traits (don't decay, modify scoring). They share the `Motivation` buffer and `decayRate` is meaningless for half of them. Buffer fattens; scoring code special-cases.
- [x] Split `NeedType` (buffer, scored each frame) from traits. Renamed `MotivationType` → `NeedType` across 35 files; removed 9 unused trait enum values (Bookworm, NightOwl, EarlyBird, Glutton, Grumpy, Depressed, Lazy, Nervous, Slob); renamed `motivationType` field → `needType` everywhere; renamed `InteractionBlob.satisfiedMotivation` → `satisfiedNeed`; added `[FormerlySerializedAs]` on SO fields. Traits remain as float fields on `Personality` struct — add `TraitType` enum later when content needs it. (Done 2026-05-29.)
- **Touches:** `Motivation` buffer in `AiComponents.cs:35`, `MotivationDecaySystem`, `MotivationScoringSystem`, `PersonalityContextSystem`, `Personality.bravery` (see [[Memories/Code/Bravery System]]).

### 3. Unit archetype width
- **Problem:** `AiComponents.cs` alone adds ~20 components/buffers to AI units before Combat/Health/Movement/Animation. Wide archetypes → fewer entities per 16KB chunk → hurts `ScheduleParallel` throughput more than any per-system optimization.
- [ ] Spot-check citizen prefab in Entity Debugger. If component count >40, audit and consider consolidating empty tags or splitting archetypes.

### 4. Awareness systems and N×M scan
- **Where:** `Assets/_Scripts/Systems/AIActionSelectionSystemGroup/AIAwarenessSystemGroup/ItemAwarenessSystem.cs:49–53` (and likely the other awareness systems use the same pattern).
- **Problem:** Pulls all items into NativeArrays, then job iterates AI-brains × items. O(brains × items) per frame. 50×100=5k checks fine. 2000×2000=4M painful.
- [x] Added `itemCells: NativeParallelMultiHashMap<int2, Entity>` to `SpatialHashRegistry`; `RegisterItemsJob` in `InteractionSpatialHashSystem` bins loose items by cell each frame; `ItemAwarenessSystem` now queries spatial cells (same pattern as `NavigationAwarenessSystem`) instead of iterating all items. EnemyAwarenessSystem and SocialAwarenessSystem still use faction multimap — spatial extension for those left for later. (Done 2026-05-29.)

### 5. PostBakingSystemGroup proliferation
- **Where:** 8 library bakers, all identical shape: `Dictionary<int, FooSO>` → `BlobBuilder` → `BlobArray<FooBlob>`.
- [x] Extracted `BlobLibraryUtils` static class (`Data/Structs/BlobLibraryUtils.cs`) with `BuildEnumLookup`, `EnumCount`, `FillWithPreFill`, `FillWithLookup`. Refactored Attack (FillWithPreFill + static mappers), Item (FillWithLookup + static mappers), Effect (lookup/count only — nested BlobArray stays inline), Interaction (lookup/count only — nested BlobArray stays inline). Animation, Unit, Scoring untouched (unique fill patterns). (Done 2026-05-29.)
- **Scope note:** `SystemAPI.Query<T>()` is source-generator–bound to the ISystem struct, so the query loops cannot move to a static helper. Real reduction is ~30–35% per targeted system. Full 60% would require switching to managed `SystemBase` + inheritance.

---

## Fix soon (low cost, growing tax)

### 6. "Equipt" typo is load-bearing
- **Symbols:** `UnitEquipt`, `EquiptBy`, `EquiptSocket`, `ItemEquiptSystemGroup` — and `Unequip`/`PlayerUnequipSystem` sit correctly-spelled next to them.
- [x] Global rename to `Equip*` while it's only ~15 callsites. Cost grows monthly.
- **Command:** `git grep -i equipt` to find all sites. (Done 2026-05-29 — 76 occurrences across 18 files, zero remaining.)

### 7. `ThrownItemSystem` / `ThrownItemHitSystem` sit naked in `ItemSystemGroup`
- **Where:** `Assets/_Scripts/Systems/ItemSystemGroup/ThrownItemSystem.cs`, `ThrownItemHitSystem.cs`.
- **Problem:** Everything else in `ItemSystemGroup` is in a subgroup. Inconsistency hides ordering intent.
- [x] Moved under `ThrownItemSystemGroup` (ordered after `ItemEquipSystemGroup`); files relocated to `ItemSystemGroup/ThrownItemSystemGroup/`. (Done 2026-05-29.)

### 8. `ActionType.None` co-exists with `IdleAction` tag
- **Where:** `AiEnums.cs:29` + `AiComponents.cs:78`.
- **Problem:** Dual representation forces every consumer to check both for "doing nothing."
- [x] Retired `ActionType.None`; `ActionType.Idle` (now index 0) is the single "no action" state. Validation failures in `ValidateInteractionJob` and `ValidateSocialJob` now set `ActionType.Idle`. `SetupActionJob` bounds check updated to `>= 0` so `IdleEnable` fires correctly. `IsIdleAction()` simplified. `GetActionByAttack` miss-path returns `ActionType.Idle`. (Done 2026-05-29.)

---

## Low priority but worth knowing

- **Per-frame singleton fetches** (`SystemAPI.GetSingleton<ItemLibrary>()` etc.) are cheap. If profiler ever shows a system fetching 5+ libraries per `OnUpdate`, cache as system fields and refresh only on bake.
- **`ActionOption` buffer churn** — `ClearOptionsSystem` clears every frame, awareness re-fills, scoring prunes. Lots of write traffic on every AI entity per frame. If hot, switch to "stable options + dirty flag" model.

---

## Transferability principle

The library blob pattern + ScriptableObject authoring means designers can ship content without touching code. **Biggest risk:** per-prefab overrides creeping back in (like the `ItemAuthoring` throw fields removed this session). Every "just let me override X on the prefab" starts a two-source-of-truth bug.

When reviewing PRs that add authoring fields, ask: *is this static config (→ SO) or runtime context (→ component)?* If static, it belongs on the SO and gets baked into the library blob.

---

## Recommended next pickups (highest upside / least churn)

1. ~~**#6 Equipt rename**~~ — Done 2026-05-29.
2. ~~**#7 ThrownItemSystemGroup**~~ — Done 2026-05-29.
3. ~~**#5 BlobLibraryUtils**~~ — Done 2026-05-29.
4. ~~**#2 NeedType / TraitType split**~~ — Done 2026-05-29.
5. ~~**#4 Spatial hash extension (items)**~~ — Done 2026-05-29. Enemy/social faction spatial extension deferred.
6. ~~**#1 Dual-registration sync hazard**~~ — `SelectionFunctions.PopulateFunctionTable` extracted. Done 2026-05-29.
